using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;

namespace DevLaunch.Api.Services;

/// <summary>Abstracts all Kubernetes API calls — mockable for tests.</summary>
public interface IKubernetesService
{
    Task ApplyApplicationAsync(Models.Application app, CancellationToken ct = default);
    Task DeleteApplicationAsync(string name, string ns, CancellationToken ct = default);
    Task<LiveStatusDto?> GetLiveStatusAsync(string name, string ns, CancellationToken ct = default);
    Task<string> GetLogsAsync(string name, string ns, int lines = 100, CancellationToken ct = default);
    Task<List<string>> GetEventsAsync(string name, string ns, CancellationToken ct = default);
    Task<bool> IsReachableAsync(CancellationToken ct = default);
}
