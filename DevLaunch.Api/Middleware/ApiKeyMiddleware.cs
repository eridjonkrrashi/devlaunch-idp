using DevLaunch.Api.Data;
using DevLaunch.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DevLaunch.Api.Middleware;

/// <summary>
/// Validates the X-API-Key header on every request.
/// Public endpoints (health, ready, metrics, swagger) bypass auth.
/// Sets ProjectContext in HttpContext.Items on success.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
{
    private static readonly HashSet<string> _publicPrefixes =
    [
        "/health", "/ready", "/metrics", "/swagger", "/favicon.ico"
    ];

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (_publicPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(ctx);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "X-API-Key header is required." });
            return;
        }

        var hash = HashKey(rawKey!);

        await using var scope = ctx.RequestServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var key = await db.ApiKeys
            .Include(k => k.Project)
            .FirstOrDefaultAsync(k => k.KeyHash == hash);

        if (key is null || key.IsRevoked || key.Project is null)
        {
            logger.LogWarning("Invalid or revoked API key attempt from {Ip}", ctx.Connection.RemoteIpAddress);
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid or revoked API key." });
            return;
        }

        // Update LastUsedAt asynchronously (fire-and-forget, don't block the request)
        _ = UpdateLastUsedAsync(ctx.RequestServices, key.Id);

        ctx.Items[nameof(ProjectContext)] = new ProjectContext
        {
            ProjectId = key.Project.Id,
            ProjectName = key.Project.Name,
            Namespace = key.Project.Namespace,
            ApiKeyId = key.Id,
            KeyPrefix = key.KeyPrefix,
            Role = key.Role
        };

        await next(ctx);
    }

    internal static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task UpdateLastUsedAsync(IServiceProvider sp, Guid keyId)
    {
        try
        {
            await using var scope = sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ApiKeys.Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow));
        }
        catch { /* non-critical */ }
    }
}

public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder app)
        => app.UseMiddleware<ApiKeyMiddleware>();
}
