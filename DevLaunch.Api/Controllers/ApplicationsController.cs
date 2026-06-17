using DevLaunch.Api.DTOs;
using DevLaunch.Api.Middleware;
using DevLaunch.Api.Models;
using DevLaunch.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DevLaunch.Api.Controllers;

/// <summary>Manages the full lifecycle of developer-deployed applications, scoped to the authenticated project.</summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ApplicationsController(
    ApplicationService appService,
    IKubernetesService k8sService,
    ILogger<ApplicationsController> logger) : ControllerBase
{
    /// <summary>Deploy a new application into the authenticated project's namespace.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApplicationDetailDto), 201)]
    [ProducesResponseType(typeof(ApiError), 400)]
    [ProducesResponseType(typeof(ApiError), 409)]
    public async Task<IActionResult> Create([FromBody] ApplicationSpec spec, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        try
        {
            var app = await appService.CreateAsync(ctx.ProjectId, spec, ctx.ApiKeyId, ct);
            return CreatedAtAction(nameof(GetByName), new { name = app.Name }, await ToDetailDtoAsync(app, ct));
        }
        catch (InvalidOperationException ex) { return Conflict(Error(ex.Message)); }
        catch (DbUpdateException) { return Conflict(Error($"Application '{spec.Name}' already exists.")); }
        catch (QuotaExceededException ex) { return StatusCode(429, Error(ex.Message)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create application {Name}", spec.Name);
            return StatusCode(500, Error("Deploy failed", ex.Message));
        }
    }

    /// <summary>List all applications in the authenticated project.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ApplicationSummaryDto>), 200)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        var apps = await appService.ListAsync(ctx.ProjectId, ct);
        var dtos = new List<ApplicationSummaryDto>(apps.Count);

        foreach (var app in apps)
        {
            var live = await k8sService.GetLiveStatusAsync(app.Name, app.Namespace, ct);
            dtos.Add(ToSummaryDto(app, live));
        }

        return Ok(dtos);
    }

    /// <summary>Get full details for a single application including live status and rollout phase.</summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ApplicationDetailDto), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> GetByName(string name, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        var app = await appService.GetAsync(ctx.ProjectId, name, ct);
        if (app is null) return NotFound(Error($"Application '{name}' not found."));
        return Ok(await ToDetailDtoAsync(app, ct));
    }

    /// <summary>Update an application's spec and roll out a new revision.</summary>
    [HttpPut("{name}")]
    [ProducesResponseType(typeof(ApplicationDetailDto), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> Update(string name, [FromBody] ApplicationSpec spec, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        try
        {
            spec.Name = name;
            var app = await appService.UpdateAsync(ctx.ProjectId, name, spec, ctx.ApiKeyId, ct);
            return Ok(await ToDetailDtoAsync(app, ct));
        }
        catch (KeyNotFoundException ex) { return NotFound(Error(ex.Message)); }
        catch (QuotaExceededException ex) { return StatusCode(429, Error(ex.Message)); }
        catch (Exception ex) { return StatusCode(500, Error("Update failed", ex.Message)); }
    }

    /// <summary>Scale an application (disables HPA if enabled).</summary>
    [HttpPost("{name}/scale")]
    [ProducesResponseType(typeof(ApplicationSummaryDto), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> Scale(string name, [FromBody] ScaleRequest req, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        try
        {
            var app = await appService.ScaleAsync(ctx.ProjectId, name, req.Replicas, ctx.ApiKeyId, ct);
            var live = await k8sService.GetLiveStatusAsync(app.Name, app.Namespace, ct);
            return Ok(ToSummaryDto(app, live));
        }
        catch (KeyNotFoundException ex) { return NotFound(Error(ex.Message)); }
    }

    /// <summary>Roll back to a previous revision. Omit revision to go to the immediately preceding one.</summary>
    [HttpPost("{name}/rollback")]
    [ProducesResponseType(typeof(ApplicationDetailDto), 200)]
    [ProducesResponseType(typeof(ApiError), 400)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> Rollback(string name, [FromBody] RollbackRequest req, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        try
        {
            var app = await appService.RollbackAsync(ctx.ProjectId, name, req.Revision, ctx.ApiKeyId, ct);
            return Ok(await ToDetailDtoAsync(app, ct));
        }
        catch (KeyNotFoundException ex) { return NotFound(Error(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(Error(ex.Message)); }
    }

    /// <summary>Delete an application and all its Kubernetes resources.</summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> Delete(string name, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        try
        {
            await appService.DeleteAsync(ctx.ProjectId, name, ctx.ApiKeyId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(Error(ex.Message)); }
    }

    /// <summary>List deployment revisions.</summary>
    [HttpGet("{name}/revisions")]
    [ProducesResponseType(typeof(List<RevisionDto>), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> GetRevisions(string name, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        try
        {
            var revisions = await appService.GetRevisionsAsync(ctx.ProjectId, name, ct);
            return Ok(revisions.Select(r => new RevisionDto
            {
                Id = r.Id,
                RevisionNumber = r.RevisionNumber,
                Image = r.Image,
                Replicas = r.Replicas,
                CreatedAt = r.CreatedAt
            }));
        }
        catch (KeyNotFoundException ex) { return NotFound(Error(ex.Message)); }
    }

    /// <summary>Get recent pod logs (use ?lines=N to control count).</summary>
    [HttpGet("{name}/logs")]
    [ProducesResponseType(typeof(string), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> GetLogs(string name, [FromQuery] int lines = 100, CancellationToken ct = default)
    {
        var ctx = this.RequireProjectContext();
        var app = await appService.GetAsync(ctx.ProjectId, name, ct);
        if (app is null) return NotFound(Error($"Application '{name}' not found."));

        var logs = await k8sService.GetLogsAsync(name, app.Namespace, lines, ct);
        return Content(logs, "text/plain");
    }

    /// <summary>Get recent Kubernetes events for an application.</summary>
    [HttpGet("{name}/events")]
    [ProducesResponseType(typeof(List<string>), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> GetEvents(string name, CancellationToken ct)
    {
        var ctx = this.RequireProjectContext();
        var app = await appService.GetAsync(ctx.ProjectId, name, ct);
        if (app is null) return NotFound(Error($"Application '{name}' not found."));

        var events = await k8sService.GetEventsAsync(name, app.Namespace, ct);
        return Ok(events);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private async Task<ApplicationDetailDto> ToDetailDtoAsync(Models.Application app, CancellationToken ct)
    {
        var live = await k8sService.GetLiveStatusAsync(app.Name, app.Namespace, ct);
        HpaStatusDto? hpaStatus = app.HpaEnabled
            ? await k8sService.GetHpaStatusAsync(app.Name, app.Namespace, ct)
            : null;

        return new ApplicationDetailDto
        {
            Id = app.Id,
            Name = app.Name,
            Namespace = app.Namespace,
            Image = app.Image,
            Port = app.Port,
            Replicas = app.Replicas,
            Status = app.Status.ToString(),
            RolloutPhase = app.RolloutPhase.ToString(),
            RolloutMessage = app.RolloutMessage,
            CurrentRevision = app.CurrentRevision,
            CreatedAt = app.CreatedAt,
            UpdatedAt = app.UpdatedAt,
            LiveStatus = live,
            EnvVars = app.EnvVars.Select(e => new EnvVarDto { Key = e.Key, Value = e.Value }).ToList(),
            CpuRequest = app.CpuRequest,
            CpuLimit = app.CpuLimit,
            MemoryRequest = app.MemoryRequest,
            MemoryLimit = app.MemoryLimit,
            IngressHost = app.IngressHost,
            HpaEnabled = app.HpaEnabled,
            HpaMinReplicas = app.HpaMinReplicas,
            HpaMaxReplicas = app.HpaMaxReplicas,
            HpaCpuTargetPercent = app.HpaCpuTargetPercent,
            HpaStatus = hpaStatus,
            Revisions = app.Revisions.Select(r => new RevisionDto
            {
                Id = r.Id,
                RevisionNumber = r.RevisionNumber,
                Image = r.Image,
                Replicas = r.Replicas,
                CreatedAt = r.CreatedAt
            }).OrderByDescending(r => r.RevisionNumber).ToList()
        };
    }

    private static ApplicationSummaryDto ToSummaryDto(Models.Application app, LiveStatusDto? live) => new()
    {
        Id = app.Id,
        Name = app.Name,
        Namespace = app.Namespace,
        Image = app.Image,
        Port = app.Port,
        Replicas = app.Replicas,
        Status = app.Status.ToString(),
        RolloutPhase = app.RolloutPhase.ToString(),
        RolloutMessage = app.RolloutMessage,
        CurrentRevision = app.CurrentRevision,
        CreatedAt = app.CreatedAt,
        UpdatedAt = app.UpdatedAt,
        LiveStatus = live
    };

    private static ApiError Error(string msg, string? details = null) => new() { Error = msg, Details = details };
}
