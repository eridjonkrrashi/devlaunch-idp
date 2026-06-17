using System.ComponentModel.DataAnnotations;

namespace DevLaunch.Api.Models;

public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? ApiKeyId { get; set; }

    [Required, MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string TargetKind { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string TargetName { get; set; } = string.Empty;

    /// <summary>JSON object with additional context (old/new values, etc.).</summary>
    public string? Details { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
