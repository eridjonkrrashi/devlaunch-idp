using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevLaunch.Api.Data;
using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevLaunch.Tests;

/// <summary>
/// HTTP integration tests: full request pipeline (middleware → controller → service → DB),
/// mocked Kubernetes, real SQLite. Tests are scoped per-project to prevent cross-test interference.
/// </summary>
public class IntegrationTests : IClassFixture<DevLaunchFactory>
{
    private readonly DevLaunchFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public IntegrationTests(DevLaunchFactory factory) => _factory = factory;

    // ── Authentication enforcement ─────────────────────────────────────────────

    [Fact]
    public async Task Request_WithNoApiKey_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithInvalidApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "not-a-real-key");
        var response = await client.GetAsync("/api/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithRevokedKey_Returns401()
    {
        var (client, projectId) = await _factory.CreateProjectClientAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = await db.ApiKeys.FirstAsync(k => k.ProjectId == projectId);
        key.IsRevoked = true;
        await db.SaveChangesAsync();

        var response = await client.GetAsync("/api/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithValidKey_Returns200()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.GetAsync("/api/applications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Public endpoints bypass auth ──────────────────────────────────────────

    [Theory]
    [InlineData("/health")]
    [InlineData("/ready")]
    public async Task PublicEndpoints_NoKeyRequired(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Application CRUD lifecycle ────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidSpec_Returns201()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications", Spec("create-v1"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ApplicationDetailDto>(JsonOpts);
        Assert.NotNull(dto);
        Assert.Equal("create-v1", dto.Name);
        Assert.Equal("nginx:latest", dto.Image);
        Assert.Equal(1, dto.CurrentRevision);
    }

    [Fact]
    public async Task Create_ThenGet_ReturnsApp()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("get-v1"));

        var response = await client.GetAsync("/api/applications/get-v1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ApplicationDetailDto>(JsonOpts);
        Assert.Equal("get-v1", dto!.Name);
    }

    [Fact]
    public async Task List_ContainsCreatedApps()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("list-a1"));
        await client.PostAsJsonAsync("/api/applications", Spec("list-b1"));

        var apps = await client.GetFromJsonAsync<List<ApplicationSummaryDto>>(
            "/api/applications", JsonOpts);
        Assert.Contains(apps!, a => a.Name == "list-a1");
        Assert.Contains(apps!, a => a.Name == "list-b1");
    }

    [Fact]
    public async Task Update_ChangesImageAndIncrementsRevision()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("update-v1", "nginx:1.0"));

        var response = await client.PutAsJsonAsync("/api/applications/update-v1",
            Spec("update-v1", "nginx:2.0"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ApplicationDetailDto>(JsonOpts);
        Assert.Equal("nginx:2.0", dto!.Image);
        Assert.Equal(2, dto.CurrentRevision);
    }

    [Fact]
    public async Task Scale_ChangesReplicaCount()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("scale-v1"));

        var response = await client.PostAsJsonAsync("/api/applications/scale-v1/scale",
            new ScaleRequest { Replicas = 4 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ApplicationSummaryDto>(JsonOpts);
        Assert.Equal(4, dto!.Replicas);
    }

    [Fact]
    public async Task Rollback_RevertsToPreviousImage()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("rollback-v1", "nginx:1.0"));
        await client.PutAsJsonAsync("/api/applications/rollback-v1", Spec("rollback-v1", "nginx:2.0"));

        var response = await client.PostAsJsonAsync("/api/applications/rollback-v1/rollback",
            new RollbackRequest());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ApplicationDetailDto>(JsonOpts);
        Assert.Equal("nginx:1.0", dto!.Image);
    }

    [Fact]
    public async Task Delete_RemovesApp()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("delete-v1"));

        var del = await client.DeleteAsync("/api/applications/delete-v1");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await client.GetAsync("/api/applications/delete-v1");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task GetRevisions_ReturnsHistory()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("revs-v1", "nginx:1.0"));
        await client.PutAsJsonAsync("/api/applications/revs-v1", Spec("revs-v1", "nginx:2.0"));

        var revs = await client.GetFromJsonAsync<List<RevisionDto>>(
            "/api/applications/revs-v1/revisions", JsonOpts);
        Assert.Equal(2, revs!.Count);
    }

    [Fact]
    public async Task GetLogs_Returns200()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("logs-v1"));

        var response = await client.GetAsync("/api/applications/logs-v1/logs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetEvents_Returns200()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("events-v1"));

        var response = await client.GetAsync("/api/applications/events-v1/events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Project isolation (multi-tenancy) ─────────────────────────────────────

    [Fact]
    public async Task TenantA_ListDoesNotShowTenantBApps()
    {
        var (clientA, _) = await _factory.CreateProjectClientAsync();
        var (clientB, _) = await _factory.CreateProjectClientAsync();

        await clientA.PostAsJsonAsync("/api/applications", Spec("tenant-secret-app"));

        var apps = await clientB.GetFromJsonAsync<List<ApplicationSummaryDto>>(
            "/api/applications", JsonOpts);
        Assert.DoesNotContain(apps!, a => a.Name == "tenant-secret-app");
    }

    [Fact]
    public async Task TenantB_CannotGetTenantAApp()
    {
        var (clientA, _) = await _factory.CreateProjectClientAsync();
        var (clientB, _) = await _factory.CreateProjectClientAsync();

        await clientA.PostAsJsonAsync("/api/applications", Spec("cross-tenant-app"));

        var response = await clientB.GetAsync("/api/applications/cross-tenant-app");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantB_CannotDeleteTenantAApp()
    {
        var (clientA, _) = await _factory.CreateProjectClientAsync();
        var (clientB, _) = await _factory.CreateProjectClientAsync();

        await clientA.PostAsJsonAsync("/api/applications", Spec("protected-app"));

        // B gets 404 (app not visible to them)
        var del = await clientB.DeleteAsync("/api/applications/protected-app");
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);

        // App still exists for A
        var get = await clientA.GetAsync("/api/applications/protected-app");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task TenantB_CannotScaleTenantAApp()
    {
        var (clientA, _) = await _factory.CreateProjectClientAsync();
        var (clientB, _) = await _factory.CreateProjectClientAsync();

        await clientA.PostAsJsonAsync("/api/applications", Spec("scale-protected"));

        var response = await clientB.PostAsJsonAsync("/api/applications/scale-protected/scale",
            new ScaleRequest { Replicas = 5 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Role-based access control ─────────────────────────────────────────────

    [Fact]
    public async Task CreateProject_WithDeveloperKey_Returns403()
    {
        var (devClient, _) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Developer);
        var response = await devClient.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest { Name = "should-be-forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOwnProject_WithDeveloperKey_Returns200()
    {
        var (devClient, projectId) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Developer);
        var response = await devClient.GetAsync($"/api/projects/{projectId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOtherProject_WithDeveloperKey_Returns403()
    {
        var (adminClient, _) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Admin);
        var (devClient, _) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Developer);

        // Admin creates another project
        var createResp = await adminClient.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest { Name = "other-" + Guid.NewGuid().ToString("N")[..6] });
        var created = await createResp.Content.ReadFromJsonAsync<ProjectDto>(JsonOpts);

        var response = await devClient.GetAsync($"/api/projects/{created!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_DeveloperCannotCreateAdminKey()
    {
        var (devClient, projectId) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Developer);
        var response = await devClient.PostAsJsonAsync($"/api/projects/{projectId}/api-keys",
            new CreateApiKeyRequest { Name = "evil-admin", Role = ApiKeyRole.Admin });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Audit log ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditLog_RecordsDeployAndDelete()
    {
        var (client, projectId) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", Spec("audit-app-v1"));
        await client.DeleteAsync("/api/applications/audit-app-v1");

        var entries = await client.GetFromJsonAsync<List<AuditEntryDto>>(
            $"/api/projects/{projectId}/audit", JsonOpts);
        Assert.NotNull(entries);
        Assert.Contains(entries, e => e.Action == "deploy");
        Assert.Contains(entries, e => e.Action == "delete");
    }

    [Fact]
    public async Task AuditLog_CrossTenantAccess_Returns403()
    {
        var (_, projectId) = await _factory.CreateProjectClientAsync();
        var (devClient, _) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Developer);

        var response = await devClient.GetAsync($"/api/projects/{projectId}/audit");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Projects API ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminListProjects_ReturnsAllProjects()
    {
        var (adminClient, _) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Admin);
        var projects = await adminClient.GetFromJsonAsync<List<ProjectDto>>("/api/projects", JsonOpts);
        Assert.NotNull(projects);
        Assert.NotEmpty(projects);
    }

    [Fact]
    public async Task DeveloperListProjects_ReturnsOnlyOwnProject()
    {
        var (devClient, devProjectId) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Developer);
        var projects = await devClient.GetFromJsonAsync<List<ProjectDto>>("/api/projects", JsonOpts);
        Assert.NotNull(projects);
        Assert.All(projects, p => Assert.Equal(devProjectId, p.Id));
    }

    [Fact]
    public async Task CreateApiKey_Returns201WithRawKey()
    {
        var (adminClient, adminProjectId) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Admin);
        var response = await adminClient.PostAsJsonAsync(
            $"/api/projects/{adminProjectId}/api-keys",
            new CreateApiKeyRequest { Name = "new-key", Role = ApiKeyRole.Developer });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ApiKeyDto>(JsonOpts);
        Assert.NotNull(dto!.RawKey);
        Assert.StartsWith("dlk_", dto.RawKey);
    }

    [Fact]
    public async Task RevokeApiKey_KeyNoLongerWorks()
    {
        var (adminClient, projectId) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Admin);

        // Create a developer key
        var createResp = await adminClient.PostAsJsonAsync($"/api/projects/{projectId}/api-keys",
            new CreateApiKeyRequest { Name = "temp-key", Role = ApiKeyRole.Developer });
        var keyDto = await createResp.Content.ReadFromJsonAsync<ApiKeyDto>(JsonOpts);

        // Use the new key
        var devClient = _factory.CreateClient();
        devClient.DefaultRequestHeaders.Add("X-API-Key", keyDto!.RawKey!);
        var before = await devClient.GetAsync("/api/applications");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Revoke it
        await adminClient.DeleteAsync($"/api/projects/{projectId}/api-keys/{keyDto.Id}");

        // Should now be rejected
        var after = await devClient.GetAsync("/api/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    // ── Error response format ─────────────────────────────────────────────────

    [Fact]
    public async Task ErrorResponse_IsJsonWithErrorField()
    {
        var response = await _factory.CreateClient().GetAsync("/api/applications");
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("error", out _),
            $"Expected 'error' field in: {body}");
    }

    // ── HPA ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithHpa_PersistsHpaSettings()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var spec = Spec("hpa-app-v1");
        spec.HpaEnabled = true;
        spec.HpaMinReplicas = 2;
        spec.HpaMaxReplicas = 8;
        spec.HpaCpuTargetPercent = 70;

        var response = await client.PostAsJsonAsync("/api/applications", spec);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ApplicationDetailDto>(JsonOpts);
        Assert.True(dto!.HpaEnabled);
        Assert.Equal(2, dto.HpaMinReplicas);
        Assert.Equal(8, dto.HpaMaxReplicas);
    }

    [Fact]
    public async Task Scale_DisablesHpa()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var spec = Spec("hpa-disable-v1");
        spec.HpaEnabled = true;
        await client.PostAsJsonAsync("/api/applications", spec);

        var response = await client.PostAsJsonAsync("/api/applications/hpa-disable-v1/scale",
            new ScaleRequest { Replicas = 3 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var get = await client.GetAsync("/api/applications/hpa-disable-v1");
        var dto = await get.Content.ReadFromJsonAsync<ApplicationDetailDto>(JsonOpts);
        Assert.False(dto!.HpaEnabled);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static ApplicationSpec Spec(string name, string image = "nginx:latest") => new()
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
