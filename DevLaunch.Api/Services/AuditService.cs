using DevLaunch.Api.Data;
using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DevLaunch.Api.Services;

public class AuditService(AppDbContext db, ILogger<AuditService> logger)
{
    public async Task RecordAsync(
        Guid projectId,
        Guid? apiKeyId,
        string action,
        string targetKind,
        string targetName,
        object? details = null,
        CancellationToken ct = default)
    {
        try
        {
            db.AuditEntries.Add(new AuditEntry
            {
                ProjectId = projectId,
                ApiKeyId = apiKeyId,
                Action = action,
                TargetKind = targetKind,
                TargetName = targetName,
                Details = details is null ? null : JsonSerializer.Serialize(details)
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write audit entry: {Action} on {Kind}/{Name}", action, targetKind, targetName);
        }
    }

    public async Task<List<AuditEntry>> GetEntriesAsync(
        Guid projectId,
        int limit = 100,
        CancellationToken ct = default)
        => await db.AuditEntries
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToListAsync(ct);
}
