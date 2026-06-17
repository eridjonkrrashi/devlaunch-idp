using Microsoft.AspNetCore.Mvc;

namespace DevLaunch.Api.Middleware;

public static class ControllerExtensions
{
    /// <summary>Returns the ProjectContext set by ApiKeyMiddleware, or null if unauthenticated.</summary>
    public static ProjectContext? GetProjectContext(this ControllerBase controller)
        => controller.HttpContext.Items[nameof(ProjectContext)] as ProjectContext;

    /// <summary>Returns the ProjectContext or throws 401 (should not happen if middleware is wired).</summary>
    public static ProjectContext RequireProjectContext(this ControllerBase controller)
        => controller.GetProjectContext()
           ?? throw new InvalidOperationException("ProjectContext not set — middleware not wired correctly.");
}
