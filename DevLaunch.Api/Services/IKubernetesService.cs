using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;

namespace DevLaunch.Api.Services;

/// <summary>Abstracts all Kubernetes API calls — mockable for tests.</summary>
public interface IKubernetesService
{
    // Application lifecycle
    Task ApplyApplicationAsync(Models.Application app, CancellationToken ct = default);
    Task DeleteApplicationAsync(string name, string ns, CancellationToken ct = default);

    // Status + observability
    Task<LiveStatusDto?> GetLiveStatusAsync(string name, string ns, CancellationToken ct = default);
    Task<HpaStatusDto?> GetHpaStatusAsync(string name, string ns, CancellationToken ct = default);
    Task<string> GetLogsAsync(string name, string ns, int lines = 100, CancellationToken ct = default);
    Task<List<string>> GetEventsAsync(string name, string ns, CancellationToken ct = default);

    // Namespace + quota management
    Task EnsureNamespaceAsync(string ns, CancellationToken ct = default);
    Task DeleteNamespaceAsync(string ns, CancellationToken ct = default);
    Task ApplyResourceQuotaAsync(string ns, string cpuQuota, string memoryQuota, CancellationToken ct = default);
    Task ApplyLimitRangeAsync(string ns, CancellationToken ct = default);

    // Cluster health
    Task<bool> IsReachableAsync(CancellationToken ct = default);
}
