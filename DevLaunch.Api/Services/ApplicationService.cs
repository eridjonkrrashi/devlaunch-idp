using DevLaunch.Api.Data;
using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using System.Text.Json;

namespace DevLaunch.Api.Services;

/// <summary>Orchestrates application lifecycle: persist → generate k8s objects → apply.</summary>
public class ApplicationService(
    AppDbContext db,
    IKubernetesService k8s,
    ILogger<ApplicationService> logger)
{
    private static readonly Counter AppsCreated = Metrics.CreateCounter(
        "devlaunch_apps_created_total", "Total applications created");
    private static readonly Counter AppsDeleted = Metrics.CreateCounter(
        "devlaunch_apps_deleted_total", "Total applications deleted");
    private static readonly Gauge ActiveApps = Metrics.CreateGauge(
        "devlaunch_active_apps", "Currently managed applications");
    private static readonly Counter DeployFailures = Metrics.CreateCounter(
        "devlaunch_deploy_failures_total", "Total deploy failures");

    public async Task<Models.Application> CreateAsync(ApplicationSpec spec, CancellationToken ct = default)
    {
        var existing = await db.Applications.AnyAsync(a => a.Name == spec.Name, ct);
        if (existing) throw new InvalidOperationException($"Application '{spec.Name}' already exists.");

        var app = MapSpecToApp(spec, new Models.Application());
        app.Status = ApplicationStatus.Deploying;
        db.Applications.Add(app);

        var revision = CreateRevision(app, 1, spec);
        db.DeploymentRevisions.Add(revision);
        await db.SaveChangesAsync(ct);

        await DeployAsync(app, ct);
        return app;
    }

    public async Task<Models.Application> UpdateAsync(string name, ApplicationSpec spec, CancellationToken ct = default)
    {
        var app = await RequireAppAsync(name, ct);
        MapSpecToApp(spec, app);
        app.Status = ApplicationStatus.Deploying;
        app.CurrentRevision++;
        app.UpdatedAt = DateTime.UtcNow;

        var revision = CreateRevision(app, app.CurrentRevision, spec);
        db.DeploymentRevisions.Add(revision);
        await db.SaveChangesAsync(ct);

        await DeployAsync(app, ct);
        return app;
    }

    public async Task<Models.Application> ScaleAsync(string name, int replicas, CancellationToken ct = default)
    {
        var app = await RequireAppAsync(name, ct);
        var spec = BuildSpec(app);
        spec.Replicas = replicas;
        return await UpdateAsync(name, spec, ct);
    }

    public async Task<Models.Application> RollbackAsync(string name, int? targetRevision, CancellationToken ct = default)
    {
        var app = await RequireAppAsync(name, ct);
        var revisions = await db.DeploymentRevisions
            .Where(r => r.ApplicationId == app.Id)
            .OrderByDescending(r => r.RevisionNumber)
            .ToListAsync(ct);

        DeploymentRevision? rev;
        if (targetRevision.HasValue)
            rev = revisions.FirstOrDefault(r => r.RevisionNumber == targetRevision.Value)
                  ?? throw new InvalidOperationException($"Revision {targetRevision} not found.");
        else
            rev = revisions.Skip(1).FirstOrDefault()
                  ?? throw new InvalidOperationException("No previous revision to roll back to.");

        var spec = JsonSerializer.Deserialize<ApplicationSpec>(rev.SpecSnapshot)
                   ?? throw new InvalidOperationException("Corrupt revision snapshot.");

        return await UpdateAsync(name, spec, ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var app = await RequireAppAsync(name, ct);
        app.Status = ApplicationStatus.Deleting;
        app.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            await k8s.DeleteApplicationAsync(app.Name, app.Namespace, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Error deleting k8s objects for {Name}: {Msg}", name, ex.Message);
        }

        db.Applications.Remove(app);
        await db.SaveChangesAsync(ct);
        AppsDeleted.Inc();
        ActiveApps.Dec();
    }

    public async Task<List<Models.Application>> ListAsync(CancellationToken ct = default)
        => await db.Applications.OrderBy(a => a.Name).ToListAsync(ct);

    public async Task<Models.Application?> GetAsync(string name, CancellationToken ct = default)
        => await db.Applications
            .Include(a => a.Revisions)
            .FirstOrDefaultAsync(a => a.Name == name, ct);

    public async Task<List<DeploymentRevision>> GetRevisionsAsync(string name, CancellationToken ct = default)
    {
        var app = await RequireAppAsync(name, ct);
        return await db.DeploymentRevisions
            .Where(r => r.ApplicationId == app.Id)
            .OrderByDescending(r => r.RevisionNumber)
            .ToListAsync(ct);
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private async Task DeployAsync(Models.Application app, CancellationToken ct)
    {
        try
        {
            await k8s.ApplyApplicationAsync(app, ct);
            app.Status = ApplicationStatus.Running;
            app.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            AppsCreated.Inc();
            ActiveApps.Inc();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deploy failed for {Name}", app.Name);
            app.Status = ApplicationStatus.Failed;
            app.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            DeployFailures.Inc();
            throw;
        }
    }

    private static Models.Application MapSpecToApp(ApplicationSpec spec, Models.Application app)
    {
        app.Name = spec.Name;
        app.Namespace = spec.Namespace;
        app.Image = spec.Image;
        app.Port = spec.Port;
        app.Replicas = spec.Replicas;
        app.EnvVars = spec.EnvVars.Select(e => new EnvVar { Key = e.Key, Value = e.Value }).ToList();
        app.CpuRequest = spec.CpuRequest;
        app.CpuLimit = spec.CpuLimit;
        app.MemoryRequest = spec.MemoryRequest;
        app.MemoryLimit = spec.MemoryLimit;
        app.IngressHost = spec.IngressHost;
        return app;
    }

    private static DeploymentRevision CreateRevision(Models.Application app, int revNum, ApplicationSpec spec)
        => new()
        {
            ApplicationId = app.Id,
            RevisionNumber = revNum,
            Image = app.Image,
            Replicas = app.Replicas,
            SpecSnapshot = JsonSerializer.Serialize(spec)
        };

    private static ApplicationSpec BuildSpec(Models.Application app) => new()
    {
        Name = app.Name,
        Namespace = app.Namespace,
        Image = app.Image,
        Port = app.Port,
        Replicas = app.Replicas,
        EnvVars = app.EnvVars.Select(e => new EnvVarDto { Key = e.Key, Value = e.Value }).ToList(),
        CpuRequest = app.CpuRequest,
        CpuLimit = app.CpuLimit,
        MemoryRequest = app.MemoryRequest,
        MemoryLimit = app.MemoryLimit,
        IngressHost = app.IngressHost
    };

    private async Task<Models.Application> RequireAppAsync(string name, CancellationToken ct)
        => await db.Applications.FirstOrDefaultAsync(a => a.Name == name, ct)
           ?? throw new KeyNotFoundException($"Application '{name}' not found.");
}
