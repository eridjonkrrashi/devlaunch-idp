using DevLaunch.Api.Data;
using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using DevLaunch.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevLaunch.Tests;

/// <summary>Unit tests for ProjectService — project CRUD, API key management, and bootstrap.</summary>
public class ProjectServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IKubernetesService> _k8s;
    private readonly ProjectService _sut;

    public ProjectServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(opts);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _k8s = new Mock<IKubernetesService>();
        _k8s.Setup(k => k.EnsureNamespaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _k8s.Setup(k => k.ApplyResourceQuotaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _k8s.Setup(k => k.ApplyLimitRangeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _k8s.Setup(k => k.DeleteNamespaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new ProjectService(_db, _k8s.Object, NullLogger<ProjectService>.Instance);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    // ── Project CRUD ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateProject_PersistsProjectWithNameAsNamespace()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "my-project" });

        Assert.Equal("my-project", project.Name);
        Assert.Equal("my-project", project.Namespace);
        Assert.Single(await _db.Projects.ToListAsync());
    }

    [Fact]
    public async Task CreateProject_CreatesK8sNamespaceAndQuota()
    {
        await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "ns-project" });

        _k8s.Verify(k => k.EnsureNamespaceAsync("ns-project", It.IsAny<CancellationToken>()), Times.Once);
        _k8s.Verify(k => k.ApplyResourceQuotaAsync("ns-project", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _k8s.Verify(k => k.ApplyLimitRangeAsync("ns-project", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateProject_ThrowsOnDuplicateName()
    {
        await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "dup-project" });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateProjectAsync(new CreateProjectRequest { Name = "dup-project" }));
    }

    [Fact]
    public async Task CreateProject_SetsCustomQuota()
    {
        var req = new CreateProjectRequest
        {
            Name = "quota-project",
            CpuQuota = "8",
            MemoryQuota = "16Gi",
            MaxApps = 50
        };
        var project = await _sut.CreateProjectAsync(req);

        Assert.Equal("8", project.CpuQuota);
        Assert.Equal("16Gi", project.MemoryQuota);
        Assert.Equal(50, project.MaxApps);
    }

    [Fact]
    public async Task CreateProject_K8sNamespaceFails_ProjectStillPersisted()
    {
        _k8s.Setup(k => k.EnsureNamespaceAsync("resilient-project", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("cluster unavailable"));

        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "resilient-project" });

        // Project exists despite k8s error (reconciler can retry later)
        Assert.NotNull(project);
        Assert.True(await _db.Projects.AnyAsync(p => p.Name == "resilient-project"));
    }

    [Fact]
    public async Task GetProject_ReturnsProjectWithApplications()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "get-proj" });
        var retrieved = await _sut.GetProjectAsync(project.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("get-proj", retrieved!.Name);
    }

    [Fact]
    public async Task GetProject_UnknownId_ReturnsNull()
    {
        Assert.Null(await _sut.GetProjectAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListProjects_ReturnsAllProjects()
    {
        await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "proj-alpha" });
        await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "proj-beta" });

        var list = await _sut.ListProjectsAsync();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task UpdateProject_ChangesDescriptionAndQuota()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "upd-project" });
        await _sut.UpdateProjectAsync(project.Id, new UpdateProjectRequest
        {
            Description = "new desc",
            CpuQuota = "16",
            MemoryQuota = "32Gi"
        });

        var updated = await _db.Projects.FindAsync(project.Id);
        Assert.Equal("new desc", updated!.Description);
        Assert.Equal("16", updated.CpuQuota);
    }

    [Fact]
    public async Task UpdateProject_UpdatesK8sQuota()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "upd-quota-proj" });
        await _sut.UpdateProjectAsync(project.Id, new UpdateProjectRequest { CpuQuota = "20", MemoryQuota = "64Gi" });

        _k8s.Verify(k => k.ApplyResourceQuotaAsync("upd-quota-proj", "20", "64Gi", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateProject_NotFound_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateProjectAsync(Guid.NewGuid(), new UpdateProjectRequest()));
    }

    [Fact]
    public async Task DeleteProject_RemovesFromDbAndDeletesNamespace()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "del-project" });
        await _sut.DeleteProjectAsync(project.Id);

        Assert.Null(await _db.Projects.FindAsync(project.Id));
        _k8s.Verify(k => k.DeleteNamespaceAsync("del-project", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteProject_NotFound_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.DeleteProjectAsync(Guid.NewGuid()));
    }

    // ── API Key management ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateApiKey_ReturnsRawKeyStartingWithDlk()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "key-project" });
        var (key, rawKey) = await _sut.CreateApiKeyAsync(project.Id,
            new CreateApiKeyRequest { Name = "test-key" });

        Assert.StartsWith("dlk_", rawKey);
        Assert.NotEmpty(key.KeyHash);
        Assert.NotEqual(rawKey, key.KeyHash);
    }

    [Fact]
    public async Task CreateApiKey_HashesRawKey()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "hash-proj" });
        var (key, rawKey) = await _sut.CreateApiKeyAsync(project.Id,
            new CreateApiKeyRequest { Name = "hashed-key" });

        Assert.Equal(ProjectService.HashKey(rawKey), key.KeyHash);
    }

    [Fact]
    public async Task CreateApiKey_DefaultRoleIsDeveloper()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "role-proj" });
        var (key, _) = await _sut.CreateApiKeyAsync(project.Id,
            new CreateApiKeyRequest { Name = "dev-key" });

        Assert.Equal(ApiKeyRole.Developer, key.Role);
    }

    [Fact]
    public async Task CreateApiKey_AdminRole_Persisted()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "admin-role-proj" });
        var (key, _) = await _sut.CreateApiKeyAsync(project.Id,
            new CreateApiKeyRequest { Name = "admin-key", Role = ApiKeyRole.Admin });

        Assert.Equal(ApiKeyRole.Admin, key.Role);
    }

    [Fact]
    public async Task CreateApiKey_ForNonExistentProject_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.CreateApiKeyAsync(Guid.NewGuid(), new CreateApiKeyRequest { Name = "key" }));
    }

    [Fact]
    public async Task RevokeApiKey_MarksKeyAsRevoked()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "revoke-proj" });
        var (key, _) = await _sut.CreateApiKeyAsync(project.Id, new CreateApiKeyRequest { Name = "torevoke" });

        await _sut.RevokeApiKeyAsync(project.Id, key.Id);

        var revoked = await _db.ApiKeys.FindAsync(key.Id);
        Assert.True(revoked!.IsRevoked);
    }

    [Fact]
    public async Task RevokeApiKey_NotFound_ThrowsKeyNotFoundException()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "revoke-404-proj" });
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.RevokeApiKeyAsync(project.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task ListApiKeys_ExcludesRevokedKeys()
    {
        var project = await _sut.CreateProjectAsync(new CreateProjectRequest { Name = "list-keys-proj" });
        var (key1, _) = await _sut.CreateApiKeyAsync(project.Id, new CreateApiKeyRequest { Name = "active" });
        var (key2, _) = await _sut.CreateApiKeyAsync(project.Id, new CreateApiKeyRequest { Name = "revoked" });
        await _sut.RevokeApiKeyAsync(project.Id, key2.Id);

        var keys = await _sut.ListApiKeysAsync(project.Id);
        Assert.Single(keys);
        Assert.Equal(key1.Id, keys[0].Id);
    }

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bootstrap_CreatesAdminProjectAndKey()
    {
        var rawKey = await _sut.EnsureBootstrapAsync();

        Assert.NotNull(rawKey);
        Assert.StartsWith("dlk_", rawKey);
        Assert.Single(await _db.Projects.ToListAsync());
        Assert.Single(await _db.ApiKeys.ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_CreatesAdminRoleKey()
    {
        await _sut.EnsureBootstrapAsync();
        var key = await _db.ApiKeys.FirstAsync();
        Assert.Equal(ApiKeyRole.Admin, key.Role);
    }

    [Fact]
    public async Task Bootstrap_SecondCall_DoesNotCreateDuplicates()
    {
        await _sut.EnsureBootstrapAsync();
        var second = await _sut.EnsureBootstrapAsync();

        Assert.Null(second);
        Assert.Equal(1, await _db.ApiKeys.CountAsync());
    }

    [Fact]
    public async Task Bootstrap_StoredKeyHash_ValidatesAgainstRawKey()
    {
        var rawKey = await _sut.EnsureBootstrapAsync();
        var key = await _db.ApiKeys.FirstAsync();

        Assert.Equal(ProjectService.HashKey(rawKey!), key.KeyHash);
    }
}
