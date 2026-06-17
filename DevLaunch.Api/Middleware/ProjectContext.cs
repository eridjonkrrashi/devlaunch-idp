using DevLaunch.Api.Models;

namespace DevLaunch.Api.Middleware;

/// <summary>
/// Populated by ApiKeyMiddleware for every authenticated request.
/// Controllers and services inject this to find the current project + role.
/// </summary>
public class ProjectContext
{
    public Guid ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string Namespace { get; init; } = string.Empty;
    public Guid ApiKeyId { get; init; }
    public string KeyPrefix { get; init; } = string.Empty;
    public ApiKeyRole Role { get; init; }

    public bool IsAdmin => Role == ApiKeyRole.Admin;
}
