using System.ComponentModel.DataAnnotations;

namespace DevLaunch.Api.Models;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(63)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(63)]
    public string Namespace { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Description { get; set; }

    // Resource quota applied to the project's k8s namespace
    [MaxLength(32)]
    public string CpuQuota { get; set; } = "4";

    [MaxLength(32)]
    public string MemoryQuota { get; set; } = "8Gi";

    public int MaxApps { get; set; } = 20;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ApiKey> ApiKeys { get; set; } = [];
    public List<Application> Applications { get; set; } = [];
    public List<AuditEntry> AuditEntries { get; set; } = [];
}
