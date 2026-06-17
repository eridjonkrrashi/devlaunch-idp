using System.ComponentModel.DataAnnotations;

namespace DevLaunch.Api.Models;

public class DeploymentRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ApplicationId { get; set; }

    public Application? Application { get; set; }

    public int RevisionNumber { get; set; }

    [Required, MaxLength(512)]
    public string Image { get; set; } = string.Empty;

    public int Replicas { get; set; }

    /// <summary>Full JSON snapshot of the ApplicationSpec at deploy time.</summary>
    public string SpecSnapshot { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
