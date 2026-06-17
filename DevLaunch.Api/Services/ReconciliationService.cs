using DevLaunch.Api.Data;
using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using Prometheus;

namespace DevLaunch.Api.Services;

/// <summary>
/// Periodic background loop: syncs status from cluster, checks rollout progress,
/// triggers auto-rollback on failed rollouts. Degrades gracefully when cluster is unreachable.
/// </summary>
public class ReconciliationService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReconciliationService> logger) : BackgroundService
{
    private static readonly Counter ReconcileErrors = Metrics.CreateCounter(
        "devlaunch_reconcile_errors_total", "Total reconciliation errors");
    private static readonly Gauge ReconcileLastRunAge = Metrics.CreateGauge(
        "devlaunch_reconcile_last_run_seconds_ago", "Seconds since last successful reconcile");

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private DateTime _lastSuccessfulRun = DateTime.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Reconciliation service started (interval: {Sec}s)", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            ReconcileLastRunAge.Set((DateTime.UtcNow - _lastSuccessfulRun).TotalSeconds);
            await ReconcileAllAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileAllAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var k8s = scope.ServiceProvider.GetRequiredService<IKubernetesService>();
            var appService = scope.ServiceProvider.GetRequiredService<ApplicationService>();

            var reachable = await k8s.IsReachableAsync(ct);
            if (!reachable)
            {
                logger.LogWarning("Kubernetes cluster unreachable — skipping reconciliation, marking apps Degraded");
                await MarkAllDegradedAsync(db, ct);
                ReconcileErrors.Inc();
                return;
            }

            var apps = await db.Applications
                .Where(a => a.Status != ApplicationStatus.Deleting)
                .ToListAsync(ct);

            foreach (var app in apps)
            {
                try { await ReconcileAppAsync(app, appService, k8s, ct); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Reconcile failed for {Name}", app.Name);
                    ReconcileErrors.Inc();
                }
            }

            await db.SaveChangesAsync(ct);
            _lastSuccessfulRun = DateTime.UtcNow;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in reconciliation loop");
            ReconcileErrors.Inc();
        }
    }

    private static async Task ReconcileAppAsync(
        Models.Application app, ApplicationService appService, IKubernetesService k8s, CancellationToken ct)
    {
        // Check rollout progress (may trigger auto-rollback)
        await appService.CheckRolloutAsync(app, ct);

        if (app.RolloutPhase == RolloutPhase.InProgress) return; // status managed by rollout checker

        var live = await k8s.GetLiveStatusAsync(app.Name, app.Namespace, ct);
        if (live is null)
        {
            if (app.Status is ApplicationStatus.Running or ApplicationStatus.Deploying)
                app.Status = ApplicationStatus.Degraded;
        }
        else
        {
            app.Status = DeriveStatus(live.ReadyReplicas, live.TotalReplicas, app.Replicas);
        }

        app.UpdatedAt = DateTime.UtcNow;
    }

    private static ApplicationStatus DeriveStatus(int ready, int total, int desired)
    {
        if (ready >= desired && total >= desired) return ApplicationStatus.Running;
        if (ready == 0) return ApplicationStatus.Failed;
        return ApplicationStatus.Degraded;
    }

    private static async Task MarkAllDegradedAsync(AppDbContext db, CancellationToken ct)
    {
        var apps = await db.Applications
            .Where(a => a.Status == ApplicationStatus.Running)
            .ToListAsync(ct);

        foreach (var app in apps)
        {
            app.Status = ApplicationStatus.Degraded;
            app.UpdatedAt = DateTime.UtcNow;
        }

        if (apps.Count > 0) await db.SaveChangesAsync(ct);
    }
}
