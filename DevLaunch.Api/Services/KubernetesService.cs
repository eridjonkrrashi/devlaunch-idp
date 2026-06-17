using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;
using k8s;
using k8s.Models;

namespace DevLaunch.Api.Services;

/// <summary>
/// Converts Application specs into Kubernetes objects and applies/removes them via the
/// official KubernetesClient. All objects are labelled with app.kubernetes.io/managed-by=devlaunch.
/// </summary>
public class KubernetesService(IKubernetes k8s, ILogger<KubernetesService> logger) : IKubernetesService
{
    private const string ManagedByLabel = "devlaunch";
    private const string AppNameLabel = "app.kubernetes.io/name";
    private const string ManagedByLabelKey = "app.kubernetes.io/managed-by";
    private const string ProjectLabel = "devlaunch.io/project";

    // ── Application lifecycle ─────────────────────────────────────────────────

    public async Task ApplyApplicationAsync(Models.Application app, CancellationToken ct = default)
    {
        await ApplyDeploymentAsync(app, ct);
        await ApplyServiceAsync(app, ct);
        if (app.HpaEnabled)
            await ApplyHpaAsync(app, ct);
        else
            await TryDeleteAsync(() => k8s.AutoscalingV1.DeleteNamespacedHorizontalPodAutoscalerAsync(app.Name, app.Namespace, cancellationToken: ct), $"HPA/{app.Name}");
        if (!string.IsNullOrWhiteSpace(app.IngressHost))
            await ApplyIngressAsync(app, ct);
    }

    public async Task DeleteApplicationAsync(string name, string ns, CancellationToken ct = default)
    {
        await TryDeleteAsync(() => k8s.AppsV1.DeleteNamespacedDeploymentAsync(name, ns, cancellationToken: ct), $"Deployment/{name}");
        await TryDeleteAsync(() => k8s.CoreV1.DeleteNamespacedServiceAsync(name, ns, cancellationToken: ct), $"Service/{name}");
        await TryDeleteAsync(() => k8s.AutoscalingV1.DeleteNamespacedHorizontalPodAutoscalerAsync(name, ns, cancellationToken: ct), $"HPA/{name}");
        await TryDeleteAsync(() => k8s.NetworkingV1.DeleteNamespacedIngressAsync(name, ns, cancellationToken: ct), $"Ingress/{name}");
    }

    // ── Status + observability ────────────────────────────────────────────────

    public async Task<LiveStatusDto?> GetLiveStatusAsync(string name, string ns, CancellationToken ct = default)
    {
        try
        {
            var deployment = await k8s.AppsV1.ReadNamespacedDeploymentAsync(name, ns, cancellationToken: ct);
            var pods = await k8s.CoreV1.ListNamespacedPodAsync(ns,
                labelSelector: $"{AppNameLabel}={name}", cancellationToken: ct);

            var podStatuses = pods.Items.Select(p => new PodStatusDto
            {
                Name = p.Metadata.Name,
                Phase = p.Status?.Phase ?? "Unknown",
                Ready = p.Status?.ContainerStatuses?.All(c => c.Ready) ?? false,
                RestartCount = p.Status?.ContainerStatuses?.Sum(c => c.RestartCount).ToString()
            }).ToList();

            var conditions = deployment.Status?.Conditions?
                .Select(c => $"{c.Type}: {c.Status} — {c.Message}")
                .ToList() ?? [];

            return new LiveStatusDto
            {
                ReadyReplicas = deployment.Status?.ReadyReplicas ?? 0,
                TotalReplicas = deployment.Status?.Replicas ?? 0,
                Pods = podStatuses,
                Conditions = conditions
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not fetch live status for {Name}/{Ns}: {Msg}", name, ns, ex.Message);
            return null;
        }
    }

    public async Task<HpaStatusDto?> GetHpaStatusAsync(string name, string ns, CancellationToken ct = default)
    {
        try
        {
            var hpa = await k8s.AutoscalingV1.ReadNamespacedHorizontalPodAutoscalerAsync(name, ns, cancellationToken: ct);
            return new HpaStatusDto
            {
                CurrentReplicas = hpa.Status?.CurrentReplicas ?? 0,
                DesiredReplicas = hpa.Status?.DesiredReplicas ?? 0,
                CurrentCpuPercent = hpa.Status?.CurrentCPUUtilizationPercentage,
                TargetCpuPercent = hpa.Spec?.TargetCPUUtilizationPercentage ?? 80
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> GetLogsAsync(string name, string ns, int lines = 100, CancellationToken ct = default)
    {
        try
        {
            var pods = await k8s.CoreV1.ListNamespacedPodAsync(ns,
                labelSelector: $"{AppNameLabel}={name}", cancellationToken: ct);

            var pod = pods.Items.FirstOrDefault(p => p.Status?.Phase == "Running")
                      ?? pods.Items.FirstOrDefault();

            if (pod is null) return "(no pods found)";

            var stream = await k8s.CoreV1.ReadNamespacedPodLogAsync(
                pod.Metadata.Name, ns, tailLines: lines, cancellationToken: ct);

            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not fetch logs for {Name}/{Ns}: {Msg}", name, ns, ex.Message);
            return $"(error fetching logs: {ex.Message})";
        }
    }

    public async Task<List<string>> GetEventsAsync(string name, string ns, CancellationToken ct = default)
    {
        try
        {
            var events = await k8s.CoreV1.ListNamespacedEventAsync(ns,
                fieldSelector: $"involvedObject.name={name}", cancellationToken: ct);

            return events.Items
                .OrderByDescending(e => e.LastTimestamp)
                .Take(20)
                .Select(e => $"[{e.LastTimestamp:u}] {e.Type}/{e.Reason}: {e.Message}")
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not fetch events for {Name}/{Ns}: {Msg}", name, ns, ex.Message);
            return [$"(error fetching events: {ex.Message})"];
        }
    }

    // ── Namespace + quota management ──────────────────────────────────────────

    public async Task EnsureNamespaceAsync(string ns, CancellationToken ct = default)
    {
        try
        {
            await k8s.CoreV1.ReadNamespaceAsync(ns, cancellationToken: ct);
            logger.LogDebug("Namespace {Ns} already exists", ns);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await k8s.CoreV1.CreateNamespaceAsync(new V1Namespace
            {
                Metadata = new V1ObjectMeta
                {
                    Name = ns,
                    Labels = new Dictionary<string, string> { [ManagedByLabelKey] = ManagedByLabel }
                }
            }, cancellationToken: ct);
            logger.LogInformation("Created namespace {Ns}", ns);
        }
    }

    public async Task DeleteNamespaceAsync(string ns, CancellationToken ct = default)
    {
        await TryDeleteAsync(() => k8s.CoreV1.DeleteNamespaceAsync(ns, cancellationToken: ct), $"Namespace/{ns}");
    }

    public async Task ApplyResourceQuotaAsync(string ns, string cpuQuota, string memoryQuota, CancellationToken ct = default)
    {
        var quota = new V1ResourceQuota
        {
            Metadata = new V1ObjectMeta
            {
                Name = "devlaunch-quota",
                NamespaceProperty = ns,
                Labels = new Dictionary<string, string> { [ManagedByLabelKey] = ManagedByLabel }
            },
            Spec = new V1ResourceQuotaSpec
            {
                Hard = new Dictionary<string, ResourceQuantity>
                {
                    ["requests.cpu"] = new ResourceQuantity(cpuQuota),
                    ["requests.memory"] = new ResourceQuantity(memoryQuota),
                    ["limits.cpu"] = new ResourceQuantity(cpuQuota),
                    ["limits.memory"] = new ResourceQuantity(memoryQuota)
                }
            }
        };

        try
        {
            await k8s.CoreV1.ReadNamespacedResourceQuotaAsync("devlaunch-quota", ns, cancellationToken: ct);
            await k8s.CoreV1.ReplaceNamespacedResourceQuotaAsync(quota, "devlaunch-quota", ns, cancellationToken: ct);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await k8s.CoreV1.CreateNamespacedResourceQuotaAsync(quota, ns, cancellationToken: ct);
        }

        logger.LogInformation("Applied ResourceQuota cpu={Cpu} mem={Mem} to {Ns}", cpuQuota, memoryQuota, ns);
    }

    public async Task ApplyLimitRangeAsync(string ns, CancellationToken ct = default)
    {
        var lr = new V1LimitRange
        {
            Metadata = new V1ObjectMeta
            {
                Name = "devlaunch-limits",
                NamespaceProperty = ns,
                Labels = new Dictionary<string, string> { [ManagedByLabelKey] = ManagedByLabel }
            },
            Spec = new V1LimitRangeSpec
            {
                Limits =
                [
                    new V1LimitRangeItem
                    {
                        Type = "Container",
                        DefaultProperty = new Dictionary<string, ResourceQuantity>
                        {
                            ["cpu"] = new ResourceQuantity("500m"),
                            ["memory"] = new ResourceQuantity("512Mi")
                        },
                        DefaultRequest = new Dictionary<string, ResourceQuantity>
                        {
                            ["cpu"] = new ResourceQuantity("100m"),
                            ["memory"] = new ResourceQuantity("128Mi")
                        },
                        Max = new Dictionary<string, ResourceQuantity>
                        {
                            ["cpu"] = new ResourceQuantity("4"),
                            ["memory"] = new ResourceQuantity("4Gi")
                        }
                    }
                ]
            }
        };

        try
        {
            await k8s.CoreV1.ReadNamespacedLimitRangeAsync("devlaunch-limits", ns, cancellationToken: ct);
            await k8s.CoreV1.ReplaceNamespacedLimitRangeAsync(lr, "devlaunch-limits", ns, cancellationToken: ct);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await k8s.CoreV1.CreateNamespacedLimitRangeAsync(lr, ns, cancellationToken: ct);
        }
    }

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            await k8s.CoreV1.ListNamespaceAsync(cancellationToken: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Manifest builders (internal so tests can verify generated objects) ────

    internal static Dictionary<string, string> BuildLabels(string name, string? projectName = null)
    {
        var labels = new Dictionary<string, string>
        {
            [AppNameLabel] = name,
            [ManagedByLabelKey] = ManagedByLabel
        };
        if (projectName is not null) labels[ProjectLabel] = projectName;
        return labels;
    }

    internal static V1Deployment BuildDeployment(Models.Application app)
    {
        var labels = BuildLabels(app.Name, app.Project?.Name);
        var envVars = app.EnvVars.Select(e => new V1EnvVar(e.Key, e.Value)).ToList();

        return new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = app.Name, NamespaceProperty = app.Namespace, Labels = labels },
            Spec = new V1DeploymentSpec
            {
                Replicas = app.Replicas,
                Selector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string> { [AppNameLabel] = app.Name }
                },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels },
                    Spec = new V1PodSpec
                    {
                        SecurityContext = new V1PodSecurityContext { RunAsNonRoot = true },
                        Containers =
                        [
                            new V1Container
                            {
                                Name = app.Name,
                                Image = app.Image,
                                Ports = [new V1ContainerPort { ContainerPort = app.Port }],
                                Env = envVars,
                                Resources = new V1ResourceRequirements
                                {
                                    Requests = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new ResourceQuantity(app.CpuRequest),
                                        ["memory"] = new ResourceQuantity(app.MemoryRequest)
                                    },
                                    Limits = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new ResourceQuantity(app.CpuLimit),
                                        ["memory"] = new ResourceQuantity(app.MemoryLimit)
                                    }
                                },
                                LivenessProbe = new V1Probe
                                {
                                    HttpGet = new V1HTTPGetAction { Path = "/health", Port = app.Port },
                                    InitialDelaySeconds = 15,
                                    PeriodSeconds = 20,
                                    FailureThreshold = 3
                                },
                                ReadinessProbe = new V1Probe
                                {
                                    HttpGet = new V1HTTPGetAction { Path = "/ready", Port = app.Port },
                                    InitialDelaySeconds = 5,
                                    PeriodSeconds = 10,
                                    FailureThreshold = 3
                                },
                                SecurityContext = new V1SecurityContext
                                {
                                    AllowPrivilegeEscalation = false,
                                    ReadOnlyRootFilesystem = false, // apps may need writable fs
                                    RunAsNonRoot = true
                                }
                            }
                        ]
                    }
                }
            }
        };
    }

    internal static V1Service BuildService(Models.Application app)
    {
        var labels = BuildLabels(app.Name, app.Project?.Name);
        return new V1Service
        {
            Metadata = new V1ObjectMeta { Name = app.Name, NamespaceProperty = app.Namespace, Labels = labels },
            Spec = new V1ServiceSpec
            {
                Selector = new Dictionary<string, string> { [AppNameLabel] = app.Name },
                Ports = [new V1ServicePort { Port = app.Port, TargetPort = app.Port }],
                Type = "ClusterIP"
            }
        };
    }

    internal static V1HorizontalPodAutoscaler BuildHpa(Models.Application app)
    {
        var labels = BuildLabels(app.Name, app.Project?.Name);
        return new V1HorizontalPodAutoscaler
        {
            Metadata = new V1ObjectMeta { Name = app.Name, NamespaceProperty = app.Namespace, Labels = labels },
            Spec = new V1HorizontalPodAutoscalerSpec
            {
                ScaleTargetRef = new V1CrossVersionObjectReference
                {
                    ApiVersion = "apps/v1",
                    Kind = "Deployment",
                    Name = app.Name
                },
                MinReplicas = app.HpaMinReplicas,
                MaxReplicas = app.HpaMaxReplicas,
                TargetCPUUtilizationPercentage = app.HpaCpuTargetPercent
            }
        };
    }

    internal static V1Ingress BuildIngress(Models.Application app)
    {
        var labels = BuildLabels(app.Name, app.Project?.Name);
        return new V1Ingress
        {
            Metadata = new V1ObjectMeta
            {
                Name = app.Name,
                NamespaceProperty = app.Namespace,
                Labels = labels,
                Annotations = new Dictionary<string, string>
                {
                    ["nginx.ingress.kubernetes.io/rewrite-target"] = "/"
                }
            },
            Spec = new V1IngressSpec
            {
                Rules =
                [
                    new V1IngressRule
                    {
                        Host = app.IngressHost,
                        Http = new V1HTTPIngressRuleValue
                        {
                            Paths =
                            [
                                new V1HTTPIngressPath
                                {
                                    Path = "/",
                                    PathType = "Prefix",
                                    Backend = new V1IngressBackend
                                    {
                                        Service = new V1IngressServiceBackend
                                        {
                                            Name = app.Name,
                                            Port = new V1ServiceBackendPort { Number = app.Port }
                                        }
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };
    }

    // ── Private apply helpers ─────────────────────────────────────────────────

    private async Task ApplyDeploymentAsync(Models.Application app, CancellationToken ct)
    {
        var deployment = BuildDeployment(app);
        try
        {
            await k8s.AppsV1.ReadNamespacedDeploymentAsync(app.Name, app.Namespace, cancellationToken: ct);
            await k8s.AppsV1.ReplaceNamespacedDeploymentAsync(deployment, app.Name, app.Namespace, cancellationToken: ct);
            logger.LogInformation("Updated Deployment {Name} in {Ns}", app.Name, app.Namespace);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await k8s.AppsV1.CreateNamespacedDeploymentAsync(deployment, app.Namespace, cancellationToken: ct);
            logger.LogInformation("Created Deployment {Name} in {Ns}", app.Name, app.Namespace);
        }
    }

    private async Task ApplyServiceAsync(Models.Application app, CancellationToken ct)
    {
        var svc = BuildService(app);
        try
        {
            await k8s.CoreV1.ReadNamespacedServiceAsync(app.Name, app.Namespace, cancellationToken: ct);
            await k8s.CoreV1.ReplaceNamespacedServiceAsync(svc, app.Name, app.Namespace, cancellationToken: ct);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await k8s.CoreV1.CreateNamespacedServiceAsync(svc, app.Namespace, cancellationToken: ct);
        }
    }

    private async Task ApplyHpaAsync(Models.Application app, CancellationToken ct)
    {
        var hpa = BuildHpa(app);
        try
        {
            await k8s.AutoscalingV1.ReadNamespacedHorizontalPodAutoscalerAsync(app.Name, app.Namespace, cancellationToken: ct);
            await k8s.AutoscalingV1.ReplaceNamespacedHorizontalPodAutoscalerAsync(hpa, app.Name, app.Namespace, cancellationToken: ct);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await k8s.AutoscalingV1.CreateNamespacedHorizontalPodAutoscalerAsync(hpa, app.Namespace, cancellationToken: ct);
        }

        logger.LogInformation("Applied HPA {Name} in {Ns} (min={Min} max={Max} cpu={Cpu}%)", app.Name, app.Namespace, app.HpaMinReplicas, app.HpaMaxReplicas, app.HpaCpuTargetPercent);
    }

    private async Task ApplyIngressAsync(Models.Application app, CancellationToken ct)
    {
        var ingress = BuildIngress(app);
        try
        {
            await k8s.NetworkingV1.ReadNamespacedIngressAsync(app.Name, app.Namespace, cancellationToken: ct);
            await k8s.NetworkingV1.ReplaceNamespacedIngressAsync(ingress, app.Name, app.Namespace, cancellationToken: ct);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await k8s.NetworkingV1.CreateNamespacedIngressAsync(ingress, app.Namespace, cancellationToken: ct);
        }
    }

    private async Task TryDeleteAsync(Func<Task> action, string resource)
    {
        try
        {
            await action();
            logger.LogInformation("Deleted {Resource}", resource);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // already gone — not an error
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not delete {Resource}: {Msg}", resource, ex.Message);
        }
    }
}
