using System.ComponentModel.DataAnnotations;

namespace DevLaunch.Api.Models;

public class Application
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Project ownership
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    [Required, MaxLength(63)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(63)]
    public string Namespace { get; set; } = "default";

    [Required, MaxLength(512)]
    public string Image { get; set; } = string.Empty;

    public int Port { get; set; } = 8080;

    public int Replicas { get; set; } = 1;

    public List<EnvVar> EnvVars { get; set; } = [];

    [MaxLength(32)]
    public string CpuRequest { get; set; } = "100m";

    [MaxLength(32)]
    public string CpuLimit { get; set; } = "500m";

    [MaxLength(32)]
    public string MemoryRequest { get; set; } = "128Mi";

    [MaxLength(32)]
    public string MemoryLimit { get; set; } = "512Mi";

    [MaxLength(256)]
    public string? IngressHost { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;

    public int CurrentRevision { get; set; } = 1;

    // Rollout tracking
    public RolloutPhase RolloutPhase { get; set; } = RolloutPhase.Unknown;
    public DateTime? RolloutStartedAt { get; set; }
    [MaxLength(512)]
    public string? RolloutMessage { get; set; }

    // Horizontal Pod Autoscaler
    public bool HpaEnabled { get; set; }
    public int HpaMinReplicas { get; set; } = 1;
    public int HpaMaxReplicas { get; set; } = 10;
    public int HpaCpuTargetPercent { get; set; } = 80;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<DeploymentRevision> Revisions { get; set; } = [];
}

public enum ApplicationStatus
{
    Pending,
    Deploying,
    Running,
    Degraded,
    Failed,
    Deleting
}

public enum RolloutPhase
{
    Unknown,
    InProgress,
    Complete,
    Failed
}

public class EnvVar
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
