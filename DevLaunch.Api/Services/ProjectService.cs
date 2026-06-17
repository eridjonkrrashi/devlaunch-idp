using DevLaunch.Api.Data;
using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DevLaunch.Api.Services;

public class ProjectService(
    AppDbContext db,
    IKubernetesService k8s,
    ILogger<ProjectService> logger)
{
    // ── Projects ─────────────────────────────────────────────────────────────

    public async Task<Project> CreateProjectAsync(CreateProjectRequest req, CancellationToken ct = default)
    {
        var ns = req.Name; // namespace matches project name (already DNS-safe)

        if (await db.Projects.AnyAsync(p => p.Name == req.Name, ct))
            throw new InvalidOperationException($"Project '{req.Name}' already exists.");

        var project = new Project
        {
            Name = req.Name,
            Namespace = ns,
            Description = req.Description,
            CpuQuota = req.CpuQuota,
            MemoryQuota = req.MemoryQuota,
            MaxApps = req.MaxApps
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        // Create the k8s namespace + ResourceQuota + LimitRange
        try
        {
            await k8s.EnsureNamespaceAsync(ns, ct);
            await k8s.ApplyResourceQuotaAsync(ns, req.CpuQuota, req.MemoryQuota, ct);
            await k8s.ApplyLimitRangeAsync(ns, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set up namespace {Ns} for project {Name}", ns, req.Name);
            // Project is persisted — k8s setup can be retried by reconciler
        }

        logger.LogInformation("Created project {Name} / namespace {Ns}", req.Name, ns);
        return project;
    }

    public async Task<Project> UpdateProjectAsync(Guid projectId, UpdateProjectRequest req, CancellationToken ct = default)
    {
        var project = await RequireProjectAsync(projectId, ct);

        if (req.Description is not null) project.Description = req.Description;
        if (req.CpuQuota is not null) project.CpuQuota = req.CpuQuota;
        if (req.MemoryQuota is not null) project.MemoryQuota = req.MemoryQuota;
        if (req.MaxApps.HasValue) project.MaxApps = req.MaxApps.Value;

        await db.SaveChangesAsync(ct);

        if (req.CpuQuota is not null || req.MemoryQuota is not null)
        {
            try { await k8s.ApplyResourceQuotaAsync(project.Namespace, project.CpuQuota, project.MemoryQuota, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not update ResourceQuota for {Ns}", project.Namespace); }
        }

        return project;
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.Include(p => p.Applications).FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new KeyNotFoundException($"Project '{projectId}' not found.");

        // Delete k8s namespace (cascades all managed resources)
        try { await k8s.DeleteNamespaceAsync(project.Namespace, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not delete namespace {Ns}", project.Namespace); }

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Deleted project {Name}", project.Name);
    }

    public Task<List<Project>> ListProjectsAsync(CancellationToken ct = default)
        => db.Projects.OrderBy(p => p.Name).ToListAsync(ct);

    public Task<Project?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
        => db.Projects.Include(p => p.Applications).FirstOrDefaultAsync(p => p.Id == projectId, ct);

    // ── API Keys ─────────────────────────────────────────────────────────────

    public async Task<(ApiKey key, string rawKey)> CreateApiKeyAsync(Guid projectId, CreateApiKeyRequest req, CancellationToken ct = default)
    {
        await RequireProjectAsync(projectId, ct);

        var raw = GenerateRawKey();
        var hash = HashKey(raw);
        var prefix = raw[..Math.Min(10, raw.Length)];

        var key = new ApiKey
        {
            ProjectId = projectId,
            KeyHash = hash,
            KeyPrefix = prefix,
            Name = req.Name,
            Role = req.Role
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created API key {Name} ({Role}) for project {ProjectId}", req.Name, req.Role, projectId);
        return (key, raw);
    }

    public async Task RevokeApiKeyAsync(Guid projectId, Guid keyId, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.ProjectId == projectId, ct)
            ?? throw new KeyNotFoundException($"API key '{keyId}' not found.");

        key.IsRevoked = true;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<ApiKey>> ListApiKeysAsync(Guid projectId, CancellationToken ct = default)
        => db.ApiKeys.Where(k => k.ProjectId == projectId && !k.IsRevoked).OrderBy(k => k.Name).ToListAsync(ct);

    // ── Bootstrap ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on startup. If no keys exist, creates a bootstrap admin project + key
    /// and logs the raw key. This is the "first run" flow.
    /// </summary>
    public async Task<string?> EnsureBootstrapAsync(CancellationToken ct = default)
    {
        if (await db.ApiKeys.AnyAsync(ct)) return null;

        logger.LogWarning("No API keys found — creating bootstrap admin project and key");

        var project = new Project
        {
            Name = "admin",
            Namespace = "devlaunch-admin",
            Description = "Bootstrap admin project",
            CpuQuota = "8",
            MemoryQuota = "16Gi",
            MaxApps = 100
        };
        db.Projects.Add(project);

        var raw = GenerateRawKey();
        var hash = HashKey(raw);

        db.ApiKeys.Add(new ApiKey
        {
            ProjectId = project.Id,
            KeyHash = hash,
            KeyPrefix = raw[..10],
            Name = "bootstrap-admin",
            Role = ApiKeyRole.Admin
        });

        await db.SaveChangesAsync(ct);
        return raw;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string GenerateRawKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "dlk_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<Project> RequireProjectAsync(Guid projectId, CancellationToken ct)
        => await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
           ?? throw new KeyNotFoundException($"Project '{projectId}' not found.");
}
