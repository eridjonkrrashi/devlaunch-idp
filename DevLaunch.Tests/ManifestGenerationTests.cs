using DevLaunch.Api.Models;
using DevLaunch.Api.Services;

namespace DevLaunch.Tests;

/// <summary>
/// Verifies that KubernetesService generates correct k8s manifests.
/// Calls the internal static builders directly — no cluster or mock needed.
/// </summary>
public class ManifestGenerationTests
{
    [Fact]
    public void BuildDeployment_SetsCorrectReplicas()
    {
        var app = MakeApp(replicas: 3);
        var d = KubernetesService.BuildDeployment(app);
        Assert.Equal(3, d.Spec.Replicas);
    }

    [Fact]
    public void BuildDeployment_SetsDevLaunchManagedByLabel()
    {
        var d = KubernetesService.BuildDeployment(MakeApp());
        Assert.Equal("devlaunch", d.Metadata.Labels["app.kubernetes.io/managed-by"]);
        Assert.Equal("test-app", d.Metadata.Labels["app.kubernetes.io/name"]);
    }

    [Fact]
    public void BuildDeployment_SetsContainerPort()
    {
        var d = KubernetesService.BuildDeployment(MakeApp(port: 3000));
        var container = d.Spec.Template.Spec.Containers.First();
        Assert.Equal(3000, container.Ports.First().ContainerPort);
    }

    [Fact]
    public void BuildDeployment_MapsEnvVars()
    {
        var app = MakeApp();
        app.EnvVars = [new EnvVar { Key = "FOO", Value = "bar" }, new EnvVar { Key = "BAZ", Value = "qux" }];
        var d = KubernetesService.BuildDeployment(app);
        var envs = d.Spec.Template.Spec.Containers.First().Env;
        Assert.Contains(envs, e => e.Name == "FOO" && e.Value == "bar");
        Assert.Contains(envs, e => e.Name == "BAZ" && e.Value == "qux");
    }

    [Fact]
    public void BuildDeployment_SetsResourceRequests()
    {
        var app = MakeApp();
        app.CpuRequest = "250m";
        app.MemoryRequest = "256Mi";
        var d = KubernetesService.BuildDeployment(app);
        var resources = d.Spec.Template.Spec.Containers.First().Resources;
        Assert.Equal("250m", resources.Requests["cpu"].Value);
        Assert.Equal("256Mi", resources.Requests["memory"].Value);
    }

    [Fact]
    public void BuildDeployment_HasLivenessAndReadinessProbes()
    {
        var d = KubernetesService.BuildDeployment(MakeApp());
        var container = d.Spec.Template.Spec.Containers.First();
        Assert.NotNull(container.LivenessProbe);
        Assert.NotNull(container.ReadinessProbe);
        Assert.Equal("/health", container.LivenessProbe!.HttpGet!.Path);
        Assert.Equal("/ready", container.ReadinessProbe!.HttpGet!.Path);
    }

    [Fact]
    public void BuildService_SetsClusterIPTypeAndCorrectPort()
    {
        var svc = KubernetesService.BuildService(MakeApp(port: 9000));
        Assert.Equal("ClusterIP", svc.Spec.Type);
        Assert.Equal(9000, svc.Spec.Ports.First().Port);
    }

    [Fact]
    public void BuildDeployment_SetsResourceLimits()
    {
        var app = MakeApp();
        app.CpuLimit = "1";
        app.MemoryLimit = "1Gi";
        var d = KubernetesService.BuildDeployment(app);
        var resources = d.Spec.Template.Spec.Containers.First().Resources;
        Assert.Equal("1", resources.Limits["cpu"].Value);
        Assert.Equal("1Gi", resources.Limits["memory"].Value);
    }

    private static Api.Models.Application MakeApp(int replicas = 1, int port = 8080) => new()
    {
        Name = "test-app",
        Namespace = "default",
        Image = "nginx:latest",
        Port = port,
        Replicas = replicas,
        CpuRequest = "100m",
        CpuLimit = "500m",
        MemoryRequest = "128Mi",
        MemoryLimit = "512Mi"
    };
}
