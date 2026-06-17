using DevLaunch.Api.DTOs;
using DevLaunch.Api.Middleware;
using DevLaunch.Api.Models;
using DevLaunch.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DevLaunch.Api.Controllers;

/// <summary>Manages projects (tenants) and their API keys. Admin role required for most operations.</summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ProjectsController(
    ProjectService projectService,
    AuditService auditService,
    ILogger<ProjectsController> logger) : ControllerBase
{
    // ── Projects ─────────────────────────────────────────────────────────────

    /// <summary>Create a new project (Admin only). Also creates the Kubernetes namespace, ResourceQuota, and LimitRange.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProjectDto), 201)]
    [ProducesResponseType(typeof(ApiError), 400)]
    [ProducesResponseType(typeof(ApiError), 403)]
    [ProducesResponseType(typeof(ApiError), 409)]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest req, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        if (!ctx.IsAdmin) return Forbid403("Only Admin keys can create projects.");

        try
        {
            var project = await projectService.CreateProjectAsync(req, ct);
            await auditService.RecordAsync(ctx.ProjectId, ctx.ApiKeyId, "create-project", "Project", req.Name, null, ct);
            return CreatedAtAction(nameof(GetById), new { id = project.Id }, ToDto(project));
        }
        catch (InvalidOperationException ex) { return Conflict(Error(ex.Message)); }
    }

    /// <summary>List all projects. Admins see all; Developers see only their own project.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ProjectDto>), 200)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();

        if (ctx.IsAdmin)
        {
            var all = await projectService.ListProjectsAsync(ct);
            return Ok(all.Select(ToDto));
        }

        var own = await projectService.GetProjectAsync(ctx.ProjectId, ct);
        return Ok(own is null ? new List<ProjectDto>() : new List<ProjectDto> { ToDto(own) });
    }

    /// <summary>Get a single project by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDto), 200)]
    [ProducesResponseType(typeof(ApiError), 403)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        if (!ctx.IsAdmin && ctx.ProjectId != id) return Forbid403();

        var project = await projectService.GetProjectAsync(id, ct);
        if (project is null) return NotFound(Error($"Project '{id}' not found."));
        return Ok(ToDto(project));
    }

    /// <summary>Update project quotas and description (Admin only).</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDto), 200)]
    [ProducesResponseType(typeof(ApiError), 403)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProjectRequest req, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        if (!ctx.IsAdmin) return Forbid403("Only Admin keys can update projects.");

        try
        {
            var project = await projectService.UpdateProjectAsync(id, req, ct);
            await auditService.RecordAsync(ctx.ProjectId, ctx.ApiKeyId, "update-project", "Project", project.Name, req, ct);
            return Ok(ToDto(project));
        }
        catch (KeyNotFoundException ex) { return NotFound(Error(ex.Message)); }
    }

    /// <summary>Delete a project and all its Kubernetes resources (Admin only).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiError), 403)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        if (!ctx.IsAdmin) return Forbid403("Only Admin keys can delete projects.");

        try
        {
            await projectService.DeleteProjectAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(Error(ex.Message)); }
    }

    // ── API Keys ─────────────────────────────────────────────────────────────

    /// <summary>Create an API key for a project. Admins can create for any project; Developers for their own.</summary>
    [HttpPost("{projectId:guid}/api-keys")]
    [ProducesResponseType(typeof(ApiKeyDto), 201)]
    [ProducesResponseType(typeof(ApiError), 403)]
    public async Task<IActionResult> CreateApiKey(Guid projectId, [FromBody] CreateApiKeyRequest req, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        if (!ctx.IsAdmin && ctx.ProjectId != projectId) return Forbid403();
        if (!ctx.IsAdmin && req.Role == ApiKeyRole.Admin) return Forbid403("Developers cannot create Admin keys.");

        try
        {
            var (key, rawKey) = await projectService.CreateApiKeyAsync(projectId, req, ct);
            await auditService.RecordAsync(ctx.ProjectId, ctx.ApiKeyId, "create-api-key", "ApiKey", req.Name,
                new { role = req.Role.ToString() }, ct);
            return CreatedAtAction(nameof(ListApiKeys), new { projectId },
                ToKeyDto(key) with { RawKey = rawKey });
        }
        catch (KeyNotFoundException ex) { return NotFound(Error(ex.Message)); }
    }

    /// <summary>List active API keys for a project (keys are shown by prefix only — raw keys are never stored).</summary>
    [HttpGet("{projectId:guid}/api-keys")]
    [ProducesResponseType(typeof(List<ApiKeyDto>), 200)]
    [ProducesResponseType(typeof(ApiError), 403)]
    public async Task<IActionResult> ListApiKeys(Guid projectId, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        if (!ctx.IsAdmin && ctx.ProjectId != projectId) return Forbid403();

        var keys = await projectService.ListApiKeysAsync(projectId, ct);
        return Ok(keys.Select(ToKeyDto));
    }

    /// <summary>Revoke an API key.</summary>
    [HttpDelete("{projectId:guid}/api-keys/{keyId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiError), 403)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> RevokeApiKey(Guid projectId, Guid keyId, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        if (!ctx.IsAdmin && ctx.ProjectId != projectId) return Forbid403();

        try
        {
            await projectService.RevokeApiKeyAsync(projectId, keyId, ct);
            await auditService.RecordAsync(ctx.ProjectId, ctx.ApiKeyId, "revoke-api-key", "ApiKey", keyId.ToString(), null, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(Error(ex.Message)); }
    }

    // ── Audit log ─────────────────────────────────────────────────────────────

    /// <summary>Get audit log for a project. Admins can see any project; Developers see their own.</summary>
    [HttpGet("{projectId:guid}/audit")]
    [ProducesResponseType(typeof(List<AuditEntryDto>), 200)]
    [ProducesResponseType(typeof(ApiError), 403)]
    public async Task<IActionResult> GetAuditLog(Guid projectId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var ctx = this.RequireProjectContext();
        if (!ctx.IsAdmin && ctx.ProjectId != projectId) return Forbid403();

        var entries = await auditService.GetEntriesAsync(projectId, Math.Min(limit, 500), ct);
        return Ok(entries.Select(e => new AuditEntryDto
        {
            Id = e.Id,
            Action = e.Action,
            TargetKind = e.TargetKind,
            TargetName = e.TargetName,
            ActorKeyPrefix = null, // loaded separately if needed
            Details = e.Details,
            Timestamp = e.Timestamp
        }));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IActionResult Forbid403(string? msg = null)
        => StatusCode(403, Error(msg ?? "Insufficient permissions."));

    private static ApiError Error(string msg) => new() { Error = msg };

    private static ProjectDto ToDto(Project p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Namespace = p.Namespace,
        Description = p.Description,
        CpuQuota = p.CpuQuota,
        MemoryQuota = p.MemoryQuota,
        MaxApps = p.MaxApps,
        AppCount = p.Applications.Count,
        CreatedAt = p.CreatedAt
    };

    private static ApiKeyDto ToKeyDto(ApiKey k) => new()
    {
        Id = k.Id,
        ProjectId = k.ProjectId,
        KeyPrefix = k.KeyPrefix,
        Name = k.Name,
        Role = k.Role.ToString(),
        IsRevoked = k.IsRevoked,
        CreatedAt = k.CreatedAt,
        LastUsedAt = k.LastUsedAt
    };
}
