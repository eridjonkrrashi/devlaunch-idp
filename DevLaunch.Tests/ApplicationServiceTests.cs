using DevLaunch.Api.Data;
using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using DevLaunch.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevLaunch.Tests;

/// <summary>Tests for ApplicationService using SQLite in-memory and a mocked k8s client.</summary>
public class ApplicationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IKubernetesService> _mockK8s;
    private readonly ApplicationService _sut;
    private readonly Project _project;
    private Guid ProjectId => _project.Id;

    public ApplicationServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(opts);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _project = new Project { Name = "test-project", Namespace = "test-ns", MaxApps = 20 };
        _db.Projects.Add(_project);
        _db.SaveChanges();

        _mockK8s = new Mock<IKubernetesService>();
        _mockK8s.Setup(k => k.ApplyApplicationAsync(It.IsAny<Api.Models.Application>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        _mockK8s.Setup(k => k.DeleteApplicationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var audit = new AuditService(_db, NullLogger<AuditService>.Instance);
        _sut = new ApplicationService(_db, _mockK8s.Object, audit, NullLogger<ApplicationService>.Instance);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── Basic CRUD ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsApplicationAndRevision()
    {
        var app = await _sut.CreateAsync(ProjectId, ValidSpec("myapp"));

        Assert.Equal("myapp", app.Name);
        Assert.Equal(1, app.CurrentRevision);
        Assert.Single(await _db.DeploymentRevisions.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_ThrowsOnDuplicateName()
    {
        await _sut.CreateAsync(ProjectId, ValidSpec("duplicate-app"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync(ProjectId, ValidSpec("duplicate-app")));
    }

    [Fact]
    public async Task CreateAsync_CallsKubernetesApply()
    {
        await _sut.CreateAsync(ProjectId, ValidSpec("k8s-app"));
        _mockK8s.Verify(k => k.ApplyApplicationAsync(It.IsAny<Api.Models.Application>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_IncrementsRevision()
    {
        await _sut.CreateAsync(ProjectId, ValidSpec("rev-app"));
        var updated = await _sut.UpdateAsync(ProjectId, "rev-app", ValidSpec("rev-app", image: "nginx:1.25"));

        Assert.Equal(2, updated.CurrentRevision);
        Assert.Equal(2, await _db.DeploymentRevisions.CountAsync());
    }

    [Fact]
    public async Task ScaleAsync_ChangesReplicaCount()
    {
        await _sut.CreateAsync(ProjectId, ValidSpec("scale-app"));
        var scaled = await _sut.ScaleAsync(ProjectId, "scale-app", 5);
        Assert.Equal(5, scaled.Replicas);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromDatabase()
    {
        await _sut.CreateAsync(ProjectId, ValidSpec("delete-app"));
        await _sut.DeleteAsync(ProjectId, "delete-app");
        Assert.Null(await _sut.GetAsync(ProjectId, "delete-app"));
    }

    [Fact]
    public async Task RollbackAsync_RevertsToPreviousRevision()
    {
        await _sut.CreateAsync(ProjectId, ValidSpec("rollback-app", image: "nginx:1.0"));
        await _sut.UpdateAsync(ProjectId, "rollback-app", ValidSpec("rollback-app", image: "nginx:2.0"));
        var rolled = await _sut.RollbackAsync(ProjectId, "rollback-app", null);

        Assert.Equal("nginx:1.0", rolled.Image);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForUnknownApp()
    {
        Assert.Null(await _sut.GetAsync(ProjectId, "nonexistent"));
    }

    // ── Multi-tenancy / project isolation ────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SetsNamespaceFromProject()
    {
        var app = await _sut.CreateAsync(ProjectId, ValidSpec("ns-app"));
        Assert.Equal(_project.Namespace, app.Namespace);
    }

    [Fact]
    public async Task GetAsync_DoesNotReturnAppsFromOtherProjects()
    {
        var other = new Project { Name = "other", Namespace = "other-ns" };
        _db.Projects.Add(other);
        await _db.SaveChangesAsync();

        await _sut.CreateAsync(ProjectId, ValidSpec("shared-name"));
        Assert.Null(await _sut.GetAsync(other.Id, "shared-name"));
    }

    [Fact]
    public async Task ListAsync_ScopedToProject()
    {
        var other = new Project { Name = "other2", Namespace = "other2-ns" };
        _db.Projects.Add(other);
        await _db.SaveChangesAsync();

        await _sut.CreateAsync(ProjectId, ValidSpec("app-a"));
        await _sut.CreateAsync(other.Id, ValidSpec("app-b"));

        var list = await _sut.ListAsync(ProjectId);
        Assert.Single(list);
        Assert.Equal("app-a", list[0].Name);
    }

    // ── Quota enforcement ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ThrowsWhenProjectQuotaExceeded()
    {
        _project.MaxApps = 1;
        await _db.SaveChangesAsync();

        await _sut.CreateAsync(ProjectId, ValidSpec("first-app"));
        await Assert.ThrowsAsync<QuotaExceededException>(
            () => _sut.CreateAsync(ProjectId, ValidSpec("second-app")));
    }

    // ── Rollout tracking ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SetsRolloutPhaseInProgress()
    {
        var app = await _sut.CreateAsync(ProjectId, ValidSpec("rollout-app"));
        Assert.Equal(RolloutPhase.InProgress, app.RolloutPhase);
        Assert.NotNull(app.RolloutStartedAt);
    }

    [Fact]
    public async Task CheckRolloutAsync_MarksCompleteWhenPodsReady()
    {
        var app = await _sut.CreateAsync(ProjectId, ValidSpec("check-app"));

        _mockK8s.Setup(k => k.GetLiveStatusAsync("check-app", _project.Namespace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveStatusDto { ReadyReplicas = 1, TotalReplicas = 1, Pods = [], Conditions = [] });

        await _sut.CheckRolloutAsync(app);

        Assert.Equal(RolloutPhase.Complete, app.RolloutPhase);
        Assert.Equal(ApplicationStatus.Running, app.Status);
    }

    [Fact]
    public async Task CheckRolloutAsync_AutoRollsBackOnCrashLoop()
    {
        await _sut.CreateAsync(ProjectId, ValidSpec("crash-app", image: "nginx:1.0"));
        var app = await _sut.UpdateAsync(ProjectId, "crash-app", ValidSpec("crash-app", image: "bad:image"));

        _mockK8s.Setup(k => k.GetLiveStatusAsync("crash-app", _project.Namespace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveStatusDto
            {
                ReadyReplicas = 0,
                TotalReplicas = 1,
                Pods = [new PodStatusDto { Name = "pod-1", Phase = "Running", Ready = false, RestartCount = "3" }],
                Conditions = []
            });

        await _sut.CheckRolloutAsync(app);

        // Auto-rollback starts a new rollout (InProgress) with the previous image
        Assert.Equal(RolloutPhase.InProgress, app.RolloutPhase);
        Assert.Equal("nginx:1.0", app.Image);
        Assert.Contains("crash-loop", app.RolloutMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Audit trail ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WritesAuditEntry()
    {
        await _sut.CreateAsync(ProjectId, ValidSpec("audited-app"));
        var entries = await _db.AuditEntries.ToListAsync();
        Assert.Single(entries);
        Assert.Equal("deploy", entries[0].Action);
        Assert.Equal("Application", entries[0].TargetKind);
    }

    [Fact]
    public async Task DeleteAsync_WritesAuditEntry()
    {
        await _sut.CreateAsync(ProjectId, ValidSpec("audit-del-app"));
        _db.ChangeTracker.Clear();
        await _sut.DeleteAsync(ProjectId, "audit-del-app");

        var entries = await _db.AuditEntries.Where(e => e.Action == "delete").ToListAsync();
        Assert.Single(entries);
    }

    // ── HPA ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScaleAsync_DisablesHpaWhenScaledManually()
    {
        var spec = ValidSpec("hpa-app");
        spec.HpaEnabled = true;
        spec.HpaMinReplicas = 1;
        spec.HpaMaxReplicas = 5;
        spec.HpaCpuTargetPercent = 70;
        await _sut.CreateAsync(ProjectId, spec);

        var scaled = await _sut.ScaleAsync(ProjectId, "hpa-app", 3);
        Assert.False(scaled.HpaEnabled);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ApplicationSpec ValidSpec(string name, string image = "nginx:latest") => new()
    {
        Name = name,
        Namespace = "default",
        Image = image,
        Port = 80,
        Replicas = 1,
        CpuRequest = "100m",
        CpuLimit = "500m",
        MemoryRequest = "128Mi",
        MemoryLimit = "512Mi"
    };
}
