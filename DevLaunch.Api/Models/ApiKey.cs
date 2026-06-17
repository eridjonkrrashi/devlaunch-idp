using System.ComponentModel.DataAnnotations;

namespace DevLaunch.Api.Models;

public enum ApiKeyRole { Admin, Developer }

public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>SHA-256 hex of the raw key — never store plaintext.</summary>
    [Required, MaxLength(64)]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>First 10 chars of the raw key for display (e.g. "dlk_a1b2c3").</summary>
    [Required, MaxLength(16)]
    public string KeyPrefix { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    public ApiKeyRole Role { get; set; } = ApiKeyRole.Developer;

    public bool IsRevoked { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }
}
