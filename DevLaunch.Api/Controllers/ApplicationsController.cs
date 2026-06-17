using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using DevLaunch.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DevLaunch.Api.Controllers;

/// <summary>Manages the full lifecycle of developer-deployed applications.</summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ApplicationsController(
    ApplicationService appService,
    IKubernetesService k8sService,
    ILogger<ApplicationsController> logger) : ControllerBase
{
    /// <summary>Create and deploy a new application from a spec.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApplicationDetailDto), 201)]
    [ProducesResponseType(typeof(ApiError), 400)]
    [ProducesResponseType(typeof(ApiError), 409)]
    public async Task<IActionResult> Create([FromBody] ApplicationSpec spec, CancellationToken ct)
    {
        try
        {
            var app = await appService.CreateAsync(spec, ct);
            return CreatedAtAction(nameof(GetByName), new { name = app.Name }, ToDetailDto(app, null));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiError { Error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create application {Name}", spec.Name);
            return StatusCode(500, new ApiError { Error = "Deploy failed", Details = ex.Message });
        }
    }

    /// <summary>List all managed applications with live status.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ApplicationSummaryDto>), 200)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var apps = await appService.ListAsync(ct);
        var dtos = new List<ApplicationSummaryDto>(apps.Count);

        foreach (var app in apps)
        {
            var live = await k8sService.GetLiveStatusAsync(app.Name, app.Namespace, ct);
            dtos.Add(ToSummaryDto(app, live));
        }

        return Ok(dtos);
    }

    /// <summary>Get full details for a single application including live status and revisions.</summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ApplicationDetailDto), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> GetByName(string name, CancellationToken ct)
    {
        var app = await appService.GetAsync(name, ct);
        if (app is null) return NotFound(new ApiError { Error = $"Application '{name}' not found." });

        var live = await k8sService.GetLiveStatusAsync(name, app.Namespace, ct);
        return Ok(ToDetailDto(app, live));
    }

    /// <summary>Update an application's spec and roll out a new revision.</summary>
    [HttpPut("{name}")]
    [ProducesResponseType(typeof(ApplicationDetailDto), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> Update(string name, [FromBody] ApplicationSpec spec, CancellationToken ct)
    {
        try
        {
            spec.Name = name;
            var app = await appService.UpdateAsync(name, spec, ct);
            var live = await k8sService.GetLiveStatusAsync(name, app.Namespace, ct);
            return Ok(ToDetailDto(app, live));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Error = $"Application '{name}' not found." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError { Error = "Update failed", Details = ex.Message });
        }
    }

    /// <summary>Scale an application to a specific replica count.</summary>
    [HttpPost("{name}/scale")]
    [ProducesResponseType(typeof(ApplicationSummaryDto), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> Scale(string name, [FromBody] ScaleRequest req, CancellationToken ct)
    {
        try
        {
            var app = await appService.ScaleAsync(name, req.Replicas, ct);
            return Ok(ToSummaryDto(app, null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Error = $"Application '{name}' not found." });
        }
    }

    /// <summary>Roll back to a previous revision (defaults to the immediately preceding one).</summary>
    [HttpPost("{name}/rollback")]
    [ProducesResponseType(typeof(ApplicationDetailDto), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    [ProducesResponseType(typeof(ApiError), 400)]
    public async Task<IActionResult> Rollback(string name, [FromBody] RollbackRequest req, CancellationToken ct)
    {
        try
        {
            var app = await appService.RollbackAsync(name, req.Revision, ct);
            var live = await k8sService.GetLiveStatusAsync(name, app.Namespace, ct);
            return Ok(ToDetailDto(app, live));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiError { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Error = ex.Message });
        }
    }

    /// <summary>Delete an application and its Kubernetes resources.</summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> Delete(string name, CancellationToken ct)
    {
        try
        {
            await appService.DeleteAsync(name, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Error = $"Application '{name}' not found." });
        }
    }

    /// <summary>List all deployment revisions for an application.</summary>
    [HttpGet("{name}/revisions")]
    [ProducesResponseType(typeof(List<RevisionDto>), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> GetRevisions(string name, CancellationToken ct)
    {
        try
        {
            var revisions = await appService.GetRevisionsAsync(name, ct);
            return Ok(revisions.Select(r => new RevisionDto
            {
                Id = r.Id,
                RevisionNumber = r.RevisionNumber,
                Image = r.Image,
                Replicas = r.Replicas,
                CreatedAt = r.CreatedAt
            }));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Error = $"Application '{name}' not found." });
        }
    }

    /// <summary>Retrieve recent pod logs (use ?lines=N to control count).</summary>
    [HttpGet("{name}/logs")]
    [ProducesResponseType(typeof(string), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> GetLogs(string name, [FromQuery] int lines = 100, CancellationToken ct = default)
    {
        var app = await appService.GetAsync(name, ct);
        if (app is null) return NotFound(new ApiError { Error = $"Application '{name}' not found." });

        var logs = await k8sService.GetLogsAsync(name, app.Namespace, lines, ct);
        return Content(logs, "text/plain");
    }

    /// <summary>Retrieve Kubernetes events for an application.</summary>
    [HttpGet("{name}/events")]
    [ProducesResponseType(typeof(List<string>), 200)]
    [ProducesResponseType(typeof(ApiError), 404)]
    public async Task<IActionResult> GetEvents(string name, CancellationToken ct)
    {
        var app = await appService.GetAsync(name, ct);
        if (app is null) return NotFound(new ApiError { Error = $"Application '{name}' not found." });

        var events = await k8sService.GetEventsAsync(name, app.Namespace, ct);
        return Ok(events);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static ApplicationSummaryDto ToSummaryDto(Models.Application app, LiveStatusDto? live) => new()
    {
        Id = app.Id,
        Name = app.Name,
        Namespace = app.Namespace,
        Image = app.Image,
        Port = app.Port,
        Replicas = app.Replicas,
        Status = app.Status.ToString(),
        CurrentRevision = app.CurrentRevision,
        CreatedAt = app.CreatedAt,
        UpdatedAt = app.UpdatedAt,
        LiveStatus = live
    };

    private static ApplicationDetailDto ToDetailDto(Models.Application app, LiveStatusDto? live) => new()
    {
        Id = app.Id,
        Name = app.Name,
        Namespace = app.Namespace,
        Image = app.Image,
        Port = app.Port,
        Replicas = app.Replicas,
        Status = app.Status.ToString(),
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
