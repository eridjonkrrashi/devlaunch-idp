using System.ComponentModel.DataAnnotations;
using DevLaunch.Api.Models;

namespace DevLaunch.Api.DTOs;

/// <summary>Request body for creating or updating an application.</summary>
public class ApplicationSpec
{
    /// <summary>DNS-safe name, 1-63 chars, lowercase alphanumeric + hyphens.</summary>
    [Required, RegularExpression(@"^[a-z][a-z0-9\-]{0,61}[a-z0-9]$|^[a-z]$",
        ErrorMessage = "Name must be a valid DNS label (lowercase, alphanumeric, hyphens, 1-63 chars).")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(63)]
    public string Namespace { get; set; } = "default";

    [Required, MinLength(1), MaxLength(512)]
    public string Image { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 8080;

    [Range(1, 50)]
    public int Replicas { get; set; } = 1;

    public List<EnvVarDto> EnvVars { get; set; } = [];

    [RegularExpression(@"^\d+m?$", ErrorMessage = "Must be a valid CPU quantity e.g. 100m or 1")]
    public string CpuRequest { get; set; } = "100m";

    [RegularExpression(@"^\d+m?$", ErrorMessage = "Must be a valid CPU quantity e.g. 500m or 1")]
    public string CpuLimit { get; set; } = "500m";

    [RegularExpression(@"^\d+(Mi|Gi|M|G)$", ErrorMessage = "Must be a valid memory quantity e.g. 128Mi")]
    public string MemoryRequest { get; set; } = "128Mi";

    [RegularExpression(@"^\d+(Mi|Gi|M|G)$", ErrorMessage = "Must be a valid memory quantity e.g. 512Mi")]
    public string MemoryLimit { get; set; } = "512Mi";

    [MaxLength(256)]
    public string? IngressHost { get; set; }
}

public class EnvVarDto
{
    [Required]
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class ScaleRequest
{
    [Range(1, 50)]
    public int Replicas { get; set; }
}

public class RollbackRequest
{
    public int? Revision { get; set; }
}

public class ApplicationSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public int Port { get; set; }
    public int Replicas { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CurrentRevision { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public LiveStatusDto? LiveStatus { get; set; }
}

public class ApplicationDetailDto : ApplicationSummaryDto
{
    public List<EnvVarDto> EnvVars { get; set; } = [];
    public string CpuRequest { get; set; } = string.Empty;
    public string CpuLimit { get; set; } = string.Empty;
    public string MemoryRequest { get; set; } = string.Empty;
    public string MemoryLimit { get; set; } = string.Empty;
    public string? IngressHost { get; set; }
    public List<RevisionDto> Revisions { get; set; } = [];
}

public class RevisionDto
{
    public Guid Id { get; set; }
    public int RevisionNumber { get; set; }
    public string Image { get; set; } = string.Empty;
    public int Replicas { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LiveStatusDto
{
    public int ReadyReplicas { get; set; }
    public int TotalReplicas { get; set; }
    public List<PodStatusDto> Pods { get; set; } = [];
    public List<string> Conditions { get; set; } = [];
}

public class PodStatusDto
{
    public string Name { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public bool Ready { get; set; }
    public string? RestartCount { get; set; }
}

public class ApiError
{
    public string Error { get; set; } = string.Empty;
    public string? Details { get; set; }
}
