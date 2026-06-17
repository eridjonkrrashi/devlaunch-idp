using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevLaunch.Api.Data;
using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using DevLaunch.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace DevLaunch.Tests;

/// <summary>
/// Failure injection and resilience tests: asserts graceful degradation when Kubernetes
/// or other dependencies fail — no crashes, correct HTTP status codes, no orphaned DB records.
/// Each test class gets its own factory so mock state changes don't bleed across classes.
/// </summary>
public class FailureInjectionTests : IDisposable
{
    private readonly DevLaunchFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    // ── Kubernetes deploy failure ─────────────────────────────────────────────

    [Fact]
    public async Task Deploy_K8sThrows_Returns500WithJsonError()
    {
        _factory.K8sMock.Setup(k => k.ApplyApplicationAsync(
            It.Is<Application>(a => a.Name == "k8s-fail"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Kubernetes API unavailable"));

        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications", ValidSpec("k8s-fail"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("error", out _),
            $"Expected JSON error envelope, got: {body}");
    }

    [Fact]
    public async Task Deploy_K8sThrows_AppMarkedFailedInDb()
    {
        _factory.K8sMock.Setup(k => k.ApplyApplicationAsync(
            It.Is<Application>(a => a.Name == "k8s-fail-db"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("cluster gone"));

        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", ValidSpec("k8s-fail-db"));

        // App is persisted with Failed status (not a crash — DB is consistent)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Name == "k8s-fail-db");
        Assert.NotNull(app);
        Assert.Equal(ApplicationStatus.Failed, app.Status);
    }

    // ── Kubernetes delete failure ─────────────────────────────────────────────

    [Fact]
    public async Task Delete_K8sThrows_StillRemovesFromDb()
    {
        _factory.K8sMock.Setup(k => k.DeleteApplicationAsync(
            It.Is<string>(n => n == "k8s-del-fail"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("k8s delete error"));

        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", ValidSpec("k8s-del-fail"));

        // Delete must succeed at the API layer even when k8s throws
        var del = await client.DeleteAsync("/api/applications/k8s-del-fail");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // App is gone from the DB
        var get = await client.GetAsync("/api/applications/k8s-del-fail");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ── Concurrent create of same app ─────────────────────────────────────────

    [Fact]
    public async Task ConcurrentCreate_SameAppName_ExactlyOneSucceeds()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var spec = ValidSpec("race-condition-app");

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => client.PostAsJsonAsync("/api/applications", spec))
            .ToList();

        var responses = await Task.WhenAll(tasks);
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflicted = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, created);
        Assert.Equal(4, conflicted);
    }

    // ── Quota exceeded ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_QuotaExceeded_Returns429()
    {
        var (client, projectId) = await _factory.CreateProjectClientAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await db.Projects.FindAsync(projectId);
        project!.MaxApps = 1;
        await db.SaveChangesAsync();

        var first = await client.PostAsJsonAsync("/api/applications", ValidSpec("quota-first"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/applications", ValidSpec("quota-second"));
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    // ── Rollout failure detection + auto-rollback ─────────────────────────────

    [Fact]
    public async Task CheckRollout_CrashLoop_AutoRollsBack()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", ValidSpec("crash-app", "nginx:1.0"));
        await client.PutAsJsonAsync("/api/applications/crash-app", ValidSpec("crash-app", "bad:image"));

        // Simulate crash-looping pods during reconciliation
        _factory.K8sMock.Setup(k => k.GetLiveStatusAsync("crash-app", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveStatusDto
            {
                ReadyReplicas = 0,
                TotalReplicas = 1,
                Pods = [new PodStatusDto { Name = "pod-1", Phase = "Running", Ready = false, RestartCount = "3" }],
                Conditions = []
            });

        // Invoke CheckRollout via service (same path as the reconciler takes)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appSvc = scope.ServiceProvider.GetRequiredService<ApplicationService>();
        var app = await db.Applications.FirstAsync(a => a.Name == "crash-app");

        await appSvc.CheckRolloutAsync(app);

        // Auto-rollback triggered: new revision is InProgress with reverted image
        Assert.Equal(RolloutPhase.InProgress, app.RolloutPhase);
        Assert.Equal("nginx:1.0", app.Image);
        Assert.Contains("crash-loop", app.RolloutMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── k8s namespace failure on project create ───────────────────────────────

    [Fact]
    public async Task CreateProject_K8sNamespaceFails_ProjectStillPersisted()
    {
        _factory.K8sMock.Setup(k => k.EnsureNamespaceAsync(
            It.Is<string>(ns => ns == "ns-fail-project"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("k8s namespace creation failed"));

        var (adminClient, _) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Admin);
        var response = await adminClient.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest { Name = "ns-fail-project" });

        // Project is persisted even though k8s namespace creation failed
        // (reconciler can retry namespace setup)
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Projects.AnyAsync(p => p.Name == "ns-fail-project"));
    }

    // ── Double-create idempotency ─────────────────────────────────────────────

    [Fact]
    public async Task Deploy_ThenRedeploy_IsIdempotent()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var spec = ValidSpec("redeploy-app", "nginx:1.0");

        var r1 = await client.PostAsJsonAsync("/api/applications", spec);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        // PUT (re-deploy same image) should succeed
        var r2 = await client.PutAsJsonAsync("/api/applications/redeploy-app",
            ValidSpec("redeploy-app", "nginx:1.0"));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        // Two k8s applies, no DB corruption
        _factory.K8sMock.Verify(k => k.ApplyApplicationAsync(
            It.Is<Application>(a => a.Name == "redeploy-app"), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── GetLiveStatus gracefully handles k8s unreachable ─────────────────────
    // IKubernetesService.GetLiveStatusAsync returns null when the cluster can't
    // be reached (the real KubernetesService wraps errors internally). The API
    // should still return 200 with LiveStatus=null rather than 500.

    [Fact]
    public async Task GetApp_WhenK8sLiveStatusReturnsNull_StillReturns200()
    {
        _factory.K8sMock.Setup(k => k.GetLiveStatusAsync(
            It.Is<string>(n => n == "live-null-app"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LiveStatusDto?)null);

        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", ValidSpec("live-null-app"));

        var response = await client.GetAsync("/api/applications/live-null-app");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ApplicationDetailDto>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Null(dto!.LiveStatus);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static ApplicationSpec ValidSpec(string name, string image = "nginx:latest") => new()
    {
        Name = name,
        Image = image,
        Port = 80,
        Replicas = 1,
        CpuRequest = "100m",
        CpuLimit = "500m",
        MemoryRequest = "128Mi",
        MemoryLimit = "512Mi"
    };
}
