using DevLaunch.Api.Data;
using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using Prometheus;

namespace DevLaunch.Api.Services;

/// <summary>
/// Periodic background loop that syncs each app's Status with live cluster state.
/// Degrades safely when the cluster is unreachable — marks apps as Degraded but
/// keeps the API responsive. Never throws; logs and continues on any error.
/// </summary>
public class ReconciliationService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReconciliationService> logger) : BackgroundService
{
    private static readonly Counter ReconcileErrors = Metrics.CreateCounter(
        "devlaunch_reconcile_errors_total", "Total reconciliation errors");

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Reconciliation service started (interval: {Sec}s)", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
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
                try
                {
                    await ReconcileAppAsync(app, k8s, db, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Reconcile failed for {Name}", app.Name);
                    ReconcileErrors.Inc();
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in reconciliation loop");
            ReconcileErrors.Inc();
        }
    }

    private static async Task ReconcileAppAsync(
        Models.Application app, IKubernetesService k8s, AppDbContext db, CancellationToken ct)
    {
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
        if (ready == desired && total == desired) return ApplicationStatus.Running;
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
