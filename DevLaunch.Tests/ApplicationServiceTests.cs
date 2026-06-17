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

    public ApplicationServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(opts);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _mockK8s = new Mock<IKubernetesService>();
        _mockK8s.Setup(k => k.ApplyApplicationAsync(It.IsAny<Api.Models.Application>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        _mockK8s.Setup(k => k.DeleteApplicationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _sut = new ApplicationService(_db, _mockK8s.Object, NullLogger<ApplicationService>.Instance);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task CreateAsync_PersistsApplicationAndRevision()
    {
        var spec = ValidSpec("myapp");
        var app = await _sut.CreateAsync(spec);

        Assert.Equal("myapp", app.Name);
        Assert.Equal(1, app.CurrentRevision);
        Assert.Single(await _db.DeploymentRevisions.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_ThrowsOnDuplicateName()
    {
        await _sut.CreateAsync(ValidSpec("duplicate-app"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync(ValidSpec("duplicate-app")));
    }

    [Fact]
    public async Task CreateAsync_CallsKubernetesApply()
    {
        await _sut.CreateAsync(ValidSpec("k8s-app"));
        _mockK8s.Verify(k => k.ApplyApplicationAsync(It.IsAny<Api.Models.Application>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_IncrementsRevision()
    {
        await _sut.CreateAsync(ValidSpec("rev-app"));
        var updated = await _sut.UpdateAsync("rev-app", ValidSpec("rev-app", image: "nginx:1.25"));

        Assert.Equal(2, updated.CurrentRevision);
        Assert.Equal(2, await _db.DeploymentRevisions.CountAsync());
    }

    [Fact]
    public async Task ScaleAsync_ChangesReplicaCount()
    {
        await _sut.CreateAsync(ValidSpec("scale-app"));
        var scaled = await _sut.ScaleAsync("scale-app", 5);
        Assert.Equal(5, scaled.Replicas);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromDatabase()
    {
        await _sut.CreateAsync(ValidSpec("delete-app"));
        await _sut.DeleteAsync("delete-app");
        Assert.Null(await _sut.GetAsync("delete-app"));
    }

    [Fact]
    public async Task RollbackAsync_RevertsToPreviousRevision()
    {
        await _sut.CreateAsync(ValidSpec("rollback-app", image: "nginx:1.0"));
        await _sut.UpdateAsync("rollback-app", ValidSpec("rollback-app", image: "nginx:2.0"));
        var rolled = await _sut.RollbackAsync("rollback-app", null);

        Assert.Equal("nginx:1.0", rolled.Image);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForUnknownApp()
    {
        Assert.Null(await _sut.GetAsync("nonexistent"));
    }

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
