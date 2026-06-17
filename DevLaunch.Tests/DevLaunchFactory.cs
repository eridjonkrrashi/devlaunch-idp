using DevLaunch.Api.Data;
using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using DevLaunch.Api.Services;
using k8s;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Cryptography;

namespace DevLaunch.Tests;

/// <summary>
/// WebApplicationFactory that replaces Kubernetes with a controllable mock and uses
/// a temp SQLite file so tests never need a real cluster or DB server.
/// </summary>
public sealed class DevLaunchFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"dltest-{Guid.NewGuid():N}.db");

    public Mock<IKubernetesService> K8sMock { get; } = new(MockBehavior.Loose);

    public DevLaunchFactory()
    {
        ResetMocks();
    }

    /// <summary>Restores all mock behaviours to safe defaults.</summary>
    public void ResetMocks()
    {
        K8sMock.Reset();
        K8sMock.Setup(k => k.IsReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        K8sMock.Setup(k => k.ApplyApplicationAsync(It.IsAny<Application>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        K8sMock.Setup(k => k.DeleteApplicationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        K8sMock.Setup(k => k.GetLiveStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((LiveStatusDto?)null);
        K8sMock.Setup(k => k.GetLogsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(string.Empty);
        K8sMock.Setup(k => k.GetEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new List<string>());
        K8sMock.Setup(k => k.EnsureNamespaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        K8sMock.Setup(k => k.DeleteNamespaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        K8sMock.Setup(k => k.ApplyResourceQuotaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        K8sMock.Setup(k => k.ApplyLimitRangeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Prevent real kubeconfig lookup
            var k8sDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IKubernetes));
            if (k8sDesc != null) services.Remove(k8sDesc);
            services.AddSingleton<IKubernetes>(_ => Mock.Of<IKubernetes>());

            // Replace k8s service with controllable mock
            var svcDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IKubernetesService));
            if (svcDesc != null) services.Remove(svcDesc);
            services.AddScoped<IKubernetesService>(_ => K8sMock.Object);

            // Remove background reconciler — prevents interference with test assertions
            var recDesc = services.SingleOrDefault(d =>
                d.ImplementationType == typeof(ReconciliationService));
            if (recDesc != null) services.Remove(recDesc);

            // Replace DB with a temp SQLite file (migrations still run at startup)
            var dbDesc = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDesc != null) services.Remove(dbDesc);
            services.AddDbContext<AppDbContext>(opts =>
                opts.UseSqlite($"Data Source={_dbPath}"));
        });
    }

    /// <summary>
    /// Inserts a project + API key directly into the DB, returns a pre-authorised HttpClient.
    /// Each call creates a unique project so tests don't share state.
    /// </summary>
    public async Task<(HttpClient client, Guid projectId)> CreateProjectClientAsync(
        string? name = null, ApiKeyRole role = ApiKeyRole.Developer)
    {
        name ??= "test-" + Guid.NewGuid().ToString("N")[..8];

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = new Project { Name = name, Namespace = name };
        db.Projects.Add(project);

        var raw = GenerateKey();
        db.ApiKeys.Add(new ApiKey
        {
            ProjectId = project.Id,
            KeyHash = ProjectService.HashKey(raw),
            KeyPrefix = raw[..10],
            Name = "test-key",
            Role = role
        });
        await db.SaveChangesAsync();

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", raw);
        return (client, project.Id);
    }

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "dlk_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    protected override void Dispose(bool disposing)
    {
        if (File.Exists(_dbPath)) try { File.Delete(_dbPath); } catch { /* best-effort */ }
        base.Dispose(disposing);
    }
}
