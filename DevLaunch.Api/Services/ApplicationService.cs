using DevLaunch.Api.Data;
using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using System.Text.Json;

namespace DevLaunch.Api.Services;

/// <summary>Orchestrates application lifecycle: validate → persist → generate k8s objects → apply.</summary>
public class ApplicationService(
    AppDbContext db,
    IKubernetesService k8s,
    AuditService audit,
    ILogger<ApplicationService> logger)
{
    private static readonly Counter AppsCreated = Metrics.CreateCounter(
        "devlaunch_apps_created_total", "Total applications created", "project");
    private static readonly Counter AppsDeleted = Metrics.CreateCounter(
        "devlaunch_apps_deleted_total", "Total applications deleted", "project");
    private static readonly Gauge ActiveApps = Metrics.CreateGauge(
        "devlaunch_active_apps", "Currently managed applications", "project");
    private static readonly Counter DeployFailures = Metrics.CreateCounter(
        "devlaunch_deploy_failures_total", "Total deploy failures", "project");

    // Rollout timeout: if not ready within this window, auto-rollback
    private static readonly TimeSpan RolloutTimeout = TimeSpan.FromMinutes(5);

    public async Task<Models.Application> CreateAsync(
        Guid projectId, ApplicationSpec spec, Guid? apiKeyId = null, CancellationToken ct = default)
    {
        var project = await RequireProjectAsync(projectId, ct);

        if (await db.Applications.AnyAsync(a => a.ProjectId == projectId && a.Name == spec.Name, ct))
            throw new InvalidOperationException($"Application '{spec.Name}' already exists in project '{project.Name}'.");

        // Quota check
        await ValidateQuotaAsync(project, spec, ct);

        spec.Namespace = project.Namespace; // always deploy into the project's namespace

        var app = MapSpecToApp(spec, new Models.Application { ProjectId = projectId });
        app.Status = ApplicationStatus.Deploying;
        app.RolloutPhase = RolloutPhase.InProgress;
        app.RolloutStartedAt = DateTime.UtcNow;
        db.Applications.Add(app);

        var revision = CreateRevision(app, 1, spec);
        db.DeploymentRevisions.Add(revision);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(projectId, apiKeyId, "deploy", "Application", app.Name,
            new { image = app.Image, replicas = app.Replicas }, ct);

        await DeployAsync(app, project.Name, ct);
        return app;
    }

    public async Task<Models.Application> UpdateAsync(
        Guid projectId, string name, ApplicationSpec spec, Guid? apiKeyId = null, CancellationToken ct = default)
    {
        var project = await RequireProjectAsync(projectId, ct);
        var app = await RequireAppAsync(projectId, name, ct);

        await ValidateQuotaAsync(project, spec, ct, excludeAppId: app.Id);

        spec.Namespace = project.Namespace;
        MapSpecToApp(spec, app);
        app.Status = ApplicationStatus.Deploying;
        app.RolloutPhase = RolloutPhase.InProgress;
        app.RolloutStartedAt = DateTime.UtcNow;
        app.CurrentRevision++;
        app.UpdatedAt = DateTime.UtcNow;

        var revision = CreateRevision(app, app.CurrentRevision, spec);
        db.DeploymentRevisions.Add(revision);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(projectId, apiKeyId, "update", "Application", name,
            new { image = app.Image, replicas = app.Replicas }, ct);

        await DeployAsync(app, project.Name, ct);
        return app;
    }

    public async Task<Models.Application> ScaleAsync(
        Guid projectId, string name, int replicas, Guid? apiKeyId = null, CancellationToken ct = default)
    {
        var app = await RequireAppAsync(projectId, name, ct);
        var spec = BuildSpec(app);
        spec.Replicas = replicas;
        spec.HpaEnabled = false; // scaling manually disables HPA
        await audit.RecordAsync(projectId, apiKeyId, "scale", "Application", name,
            new { from = app.Replicas, to = replicas }, ct);
        return await UpdateAsync(projectId, name, spec, null, ct);
    }

    public async Task<Models.Application> RollbackAsync(
        Guid projectId, string name, int? targetRevision, Guid? apiKeyId = null, CancellationToken ct = default)
    {
        var app = await RequireAppAsync(projectId, name, ct);
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

        await audit.RecordAsync(projectId, apiKeyId, "rollback", "Application", name,
            new { toRevision = rev.RevisionNumber, image = spec.Image }, ct);

        return await UpdateAsync(projectId, name, spec, null, ct);
    }

    public async Task DeleteAsync(
        Guid projectId, string name, Guid? apiKeyId = null, CancellationToken ct = default)
    {
        var app = await RequireAppAsync(projectId, name, ct);
        var projectName = app.Project?.Name ?? "";

        app.Status = ApplicationStatus.Deleting;
        app.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        try { await k8s.DeleteApplicationAsync(app.Name, app.Namespace, ct); }
        catch (Exception ex) { logger.LogWarning("Error deleting k8s objects for {Name}: {Msg}", name, ex.Message); }

        db.Applications.Remove(app);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(projectId, apiKeyId, "delete", "Application", name, null, ct);
        AppsDeleted.WithLabels(projectName).Inc();
        ActiveApps.WithLabels(projectName).Dec();
    }

    public async Task<List<Models.Application>> ListAsync(Guid projectId, CancellationToken ct = default)
        => await db.Applications.Where(a => a.ProjectId == projectId).OrderBy(a => a.Name).ToListAsync(ct);

    public async Task<Models.Application?> GetAsync(Guid projectId, string name, CancellationToken ct = default)
        => await db.Applications
            .Include(a => a.Revisions)
            .Include(a => a.Project)
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Name == name, ct);

    public async Task<List<DeploymentRevision>> GetRevisionsAsync(Guid projectId, string name, CancellationToken ct = default)
    {
        var app = await RequireAppAsync(projectId, name, ct);
        return await db.DeploymentRevisions
            .Where(r => r.ApplicationId == app.Id)
            .OrderByDescending(r => r.RevisionNumber)
            .ToListAsync(ct);
    }

    // ── Rollout management (called by ReconciliationService) ──────────────────

    public async Task CheckRolloutAsync(Models.Application app, CancellationToken ct = default)
    {
        if (app.RolloutPhase != RolloutPhase.InProgress) return;

        var live = await k8s.GetLiveStatusAsync(app.Name, app.Namespace, ct);
        if (live is null)
        {
            // Cluster unreachable — don't fail the rollout yet
            return;
        }

        if (live.ReadyReplicas >= app.Replicas)
        {
            app.RolloutPhase = RolloutPhase.Complete;
            app.RolloutMessage = null;
            app.Status = ApplicationStatus.Running;
            app.UpdatedAt = DateTime.UtcNow;
            return;
        }

        // Check for crash-looping pods
        bool hasCrashLoop = live.Pods.Any(p => int.TryParse(p.RestartCount, out var rc) && rc >= 3);

        // Check for timeout
        bool timedOut = app.RolloutStartedAt.HasValue
            && DateTime.UtcNow - app.RolloutStartedAt.Value > RolloutTimeout;

        if (hasCrashLoop || timedOut)
        {
            var reason = hasCrashLoop ? "crash-looping pods detected" : "rollout timed out";
            logger.LogWarning("Rollout failed for {Name}: {Reason}", app.Name, reason);
            app.RolloutPhase = RolloutPhase.Failed;
            app.RolloutMessage = $"Auto-rolled back: {reason}";
            app.Status = ApplicationStatus.Degraded;
            app.UpdatedAt = DateTime.UtcNow;

            // Auto-rollback: re-apply the previous revision
            try
            {
                var revisions = await db.DeploymentRevisions
                    .Where(r => r.ApplicationId == app.Id)
                    .OrderByDescending(r => r.RevisionNumber)
                    .ToListAsync(ct);

                var prev = revisions.Skip(1).FirstOrDefault();
                if (prev is not null)
                {
                    var prevSpec = JsonSerializer.Deserialize<ApplicationSpec>(prev.SpecSnapshot);
                    if (prevSpec is not null)
                    {
                        MapSpecToApp(prevSpec, app);
                        app.CurrentRevision++;
                        app.RolloutPhase = RolloutPhase.InProgress;
                        app.RolloutStartedAt = DateTime.UtcNow;
                        app.RolloutMessage = $"Auto-rolled back from revision {app.CurrentRevision - 1}: {reason}";
                        db.DeploymentRevisions.Add(CreateRevision(app, app.CurrentRevision, prevSpec));
                        await k8s.ApplyApplicationAsync(app, ct);
                        logger.LogInformation("Auto-rolled back {Name} to revision {Rev}", app.Name, app.CurrentRevision);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-rollback failed for {Name}", app.Name);
            }
        }
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private async Task DeployAsync(Models.Application app, string projectName, CancellationToken ct)
    {
        try
        {
            await k8s.ApplyApplicationAsync(app, ct);
            app.Status = ApplicationStatus.Running;
            app.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            AppsCreated.WithLabels(projectName).Inc();
            ActiveApps.WithLabels(projectName).Inc();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deploy failed for {Name}", app.Name);
            app.Status = ApplicationStatus.Failed;
            app.RolloutPhase = RolloutPhase.Failed;
            app.RolloutMessage = ex.Message;
            app.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            DeployFailures.WithLabels(projectName).Inc();
            throw;
        }
    }

    private static async Task ValidateQuotaAsync(Project project, ApplicationSpec spec, AppDbContext db, CancellationToken ct, Guid? excludeAppId = null)
    {
        var appCount = await db.Applications
            .CountAsync(a => a.ProjectId == project.Id && (excludeAppId == null || a.Id != excludeAppId), ct);

        if (appCount >= project.MaxApps)
            throw new QuotaExceededException($"Project '{project.Name}' has reached its maximum of {project.MaxApps} applications.");
    }

    private async Task ValidateQuotaAsync(Project project, ApplicationSpec spec, CancellationToken ct, Guid? excludeAppId = null)
        => await ValidateQuotaAsync(project, spec, db, ct, excludeAppId);

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
        app.HpaEnabled = spec.HpaEnabled;
        app.HpaMinReplicas = spec.HpaMinReplicas;
        app.HpaMaxReplicas = spec.HpaMaxReplicas;
        app.HpaCpuTargetPercent = spec.HpaCpuTargetPercent;
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
        IngressHost = app.IngressHost,
        HpaEnabled = app.HpaEnabled,
        HpaMinReplicas = app.HpaMinReplicas,
        HpaMaxReplicas = app.HpaMaxReplicas,
        HpaCpuTargetPercent = app.HpaCpuTargetPercent
    };

    private async Task<Project> RequireProjectAsync(Guid projectId, CancellationToken ct)
        => await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
           ?? throw new KeyNotFoundException($"Project '{projectId}' not found.");

    private async Task<Models.Application> RequireAppAsync(Guid projectId, string name, CancellationToken ct)
        => await db.Applications
               .Include(a => a.Project)
               .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Name == name, ct)
           ?? throw new KeyNotFoundException($"Application '{name}' not found.");
}

public class QuotaExceededException(string message) : Exception(message);
