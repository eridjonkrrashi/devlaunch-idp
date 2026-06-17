using System.ComponentModel.DataAnnotations;
using DevLaunch.Api.Models;

namespace DevLaunch.Api.DTOs;

public class CreateProjectRequest
{
    [Required, RegularExpression(@"^[a-z][a-z0-9\-]{0,61}[a-z0-9]$|^[a-z]$",
        ErrorMessage = "Name must be a valid DNS label (lowercase, alphanumeric, hyphens, 1-63 chars).")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Description { get; set; }

    [RegularExpression(@"^\d+m?$", ErrorMessage = "Must be a valid CPU quantity e.g. 4 or 2000m")]
    public string CpuQuota { get; set; } = "4";

    [RegularExpression(@"^\d+(Mi|Gi|M|G)$", ErrorMessage = "Must be a valid memory quantity e.g. 8Gi")]
    public string MemoryQuota { get; set; } = "8Gi";

    [Range(1, 100)]
    public int MaxApps { get; set; } = 20;
}

public class UpdateProjectRequest
{
    [MaxLength(256)]
    public string? Description { get; set; }

    [RegularExpression(@"^\d+m?$")]
    public string? CpuQuota { get; set; }

    [RegularExpression(@"^\d+(Mi|Gi|M|G)$")]
    public string? MemoryQuota { get; set; }

    [Range(1, 100)]
    public int? MaxApps { get; set; }
}

public class ProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CpuQuota { get; set; } = string.Empty;
    public string MemoryQuota { get; set; } = string.Empty;
    public int MaxApps { get; set; }
    public int AppCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateApiKeyRequest
{
    [Required, MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    public ApiKeyRole Role { get; set; } = ApiKeyRole.Developer;
}

public record ApiKeyDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string KeyPrefix { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    /// <summary>Only populated on creation — never returned again.</summary>
    public string? RawKey { get; set; }
}

public class AuditEntryDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetKind { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string? ActorKeyPrefix { get; set; }
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; }
}
