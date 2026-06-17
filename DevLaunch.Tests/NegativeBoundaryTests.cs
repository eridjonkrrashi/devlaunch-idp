using System.Net;
using System.Net.Http.Json;
using DevLaunch.Api.DTOs;
using DevLaunch.Api.Models;

namespace DevLaunch.Tests;

/// <summary>
/// Boundary and negative-input tests: every path that should reject bad input does so
/// with the correct HTTP status and a JSON error body — never a 500 crash.
/// </summary>
public class NegativeBoundaryTests : IClassFixture<DevLaunchFactory>
{
    private readonly DevLaunchFactory _factory;
    public NegativeBoundaryTests(DevLaunchFactory factory) => _factory = factory;

    // ── Invalid app names ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("UPPERCASE")]
    [InlineData("1starts-digit")]
    [InlineData("-starts-hyphen")]
    [InlineData("ends-hyphen-")]
    [InlineData("has spaces")]
    [InlineData("has_underscore")]
    // 65 chars — exceeds regex {0,61} middle + 2 boundary = 63 max
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Create_InvalidName_Returns400(string name)
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications", new ApplicationSpec
        {
            Name = name,
            Image = "nginx:latest",
            Port = 80,
            Replicas = 1,
            CpuRequest = "100m",
            CpuLimit = "500m",
            MemoryRequest = "128Mi",
            MemoryLimit = "512Mi"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Out-of-range replica counts ───────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    [InlineData(int.MaxValue)]
    public async Task Create_InvalidReplicas_Returns400(int replicas)
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications", new ApplicationSpec
        {
            Name = "replicas-test",
            Image = "nginx:latest",
            Port = 80,
            Replicas = replicas,
            CpuRequest = "100m",
            CpuLimit = "500m",
            MemoryRequest = "128Mi",
            MemoryLimit = "512Mi"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Invalid resource quantities ───────────────────────────────────────────

    [Theory]
    [InlineData("invalid", "500m", "128Mi", "512Mi")]
    [InlineData("100m", "invalid", "128Mi", "512Mi")]
    [InlineData("100m", "500m", "128bytes", "512Mi")]
    [InlineData("100m", "500m", "128Mi", "512bytes")]
    [InlineData("1.5", "500m", "128Mi", "512Mi")]   // decimals not k8s format
    [InlineData("100m", "500m", "0", "512Mi")]      // "0" not matching /^\d+(Mi|Gi|M|G)$/
    public async Task Create_InvalidResources_Returns400(
        string cpuReq, string cpuLim, string memReq, string memLim)
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications", new ApplicationSpec
        {
            Name = "resource-test",
            Image = "nginx:latest",
            Port = 80,
            Replicas = 1,
            CpuRequest = cpuReq,
            CpuLimit = cpuLim,
            MemoryRequest = memReq,
            MemoryLimit = memLim
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Empty / missing required fields ──────────────────────────────────────

    [Fact]
    public async Task Create_EmptyImage_Returns400()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications", new ApplicationSpec
        {
            Name = "img-empty",
            Image = "",
            Port = 80,
            Replicas = 1,
            CpuRequest = "100m",
            CpuLimit = "500m",
            MemoryRequest = "128Mi",
            MemoryLimit = "512Mi"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyBody_Returns400()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/applications", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Out-of-range port ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task Create_InvalidPort_Returns400(int port)
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications", new ApplicationSpec
        {
            Name = "port-test",
            Image = "nginx:latest",
            Port = port,
            Replicas = 1,
            CpuRequest = "100m",
            CpuLimit = "500m",
            MemoryRequest = "128Mi",
            MemoryLimit = "512Mi"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Injection-style strings as name (all should fail DNS validation) ──────

    [Theory]
    [InlineData("'; DROP TABLE--")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("../../../etc/passwd")]
    [InlineData("${jndi:ldap://x/a}")]
    [InlineData("😀emoji")]
    public async Task Create_InjectionString_Returns400(string name)
    {
        // All these contain chars illegal in DNS names — should be rejected
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications", new ApplicationSpec
        {
            Name = name,
            Image = "nginx:latest",
            Port = 80,
            Replicas = 1,
            CpuRequest = "100m",
            CpuLimit = "500m",
            MemoryRequest = "128Mi",
            MemoryLimit = "512Mi"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Duplicate names ───────────────────────────────────────────────────────

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var spec = ValidSpec("dup-check");
        var first = await client.PostAsJsonAsync("/api/applications", spec);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/applications", spec);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ── Non-existent resources ────────────────────────────────────────────────

    [Fact]
    public async Task Get_NonExistentApp_Returns404()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.GetAsync("/api/applications/ghost");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentApp_Returns404()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.DeleteAsync("/api/applications/ghost");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scale_NonExistentApp_Returns404()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications/ghost/scale",
            new ScaleRequest { Replicas = 2 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rollback_NonExistentApp_Returns404()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications/ghost/rollback",
            new RollbackRequest());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rollback_NoHistory_Returns400()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", ValidSpec("first-rev-only"));

        var response = await client.PostAsJsonAsync("/api/applications/first-rev-only/rollback",
            new RollbackRequest());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rollback_ToNonExistentRevision_Returns400()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", ValidSpec("rev-check"));

        var response = await client.PostAsJsonAsync("/api/applications/rev-check/rollback",
            new RollbackRequest { Revision = 99 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Scale validation ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(51)]
    public async Task Scale_InvalidReplicaCount_Returns400(int replicas)
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", ValidSpec("scale-bound"));
        var response = await client.PostAsJsonAsync("/api/applications/scale-bound/scale",
            new ScaleRequest { Replicas = replicas });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── HPA boundary validation ───────────────────────────────────────────────

    [Theory]
    [InlineData(0, 10, 80)]   // minReplicas < 1
    [InlineData(1, 0, 80)]   // maxReplicas < 1
    [InlineData(1, 10, 0)]   // cpuTarget < 1
    [InlineData(1, 10, 101)] // cpuTarget > 100
    public async Task Create_InvalidHpaSettings_Returns400(int min, int max, int cpu)
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var spec = ValidSpec("hpa-bound");
        spec.HpaEnabled = true;
        spec.HpaMinReplicas = min;
        spec.HpaMaxReplicas = max;
        spec.HpaCpuTargetPercent = cpu;
        var response = await client.PostAsJsonAsync("/api/applications", spec);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Idempotency ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_AlreadyDeleted_Returns404()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", ValidSpec("double-del"));
        await client.DeleteAsync("/api/applications/double-del");

        var second = await client.DeleteAsync("/api/applications/double-del");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Scale_ToSameValue_IsIdempotent()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        await client.PostAsJsonAsync("/api/applications", ValidSpec("idem-scale"));
        var r1 = await client.PostAsJsonAsync("/api/applications/idem-scale/scale",
            new ScaleRequest { Replicas = 2 });
        var r2 = await client.PostAsJsonAsync("/api/applications/idem-scale/scale",
            new ScaleRequest { Replicas = 2 });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    // ── Project name validation ───────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("UPPERCASE-PROJECT")]
    [InlineData("1starts-digit")]
    [InlineData("has spaces")]
    public async Task CreateProject_InvalidName_Returns400(string name)
    {
        var (adminClient, _) = await _factory.CreateProjectClientAsync(role: ApiKeyRole.Admin);
        var response = await adminClient.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest { Name = name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Error bodies are always JSON ──────────────────────────────────────────

    [Fact]
    public async Task NotFoundError_HasJsonErrorBody()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.GetAsync("/api/applications/does-not-exist-xyz");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("error", out _),
            $"Expected 'error' field in: {body}");
    }

    [Fact]
    public async Task BadRequestError_HasJsonErrorBody()
    {
        var (client, _) = await _factory.CreateProjectClientAsync();
        var response = await client.PostAsJsonAsync("/api/applications", new ApplicationSpec
        {
            Name = "INVALID_UPPERCASE",
            Image = "nginx:latest",
            Port = 80,
            Replicas = 1,
            CpuRequest = "100m",
            CpuLimit = "500m",
            MemoryRequest = "128Mi",
            MemoryLimit = "512Mi"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body), "Error response body should not be empty");
        // ASP.NET Core returns ProblemDetails JSON on model validation failure
        var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static ApplicationSpec ValidSpec(string name) => new()
    {
        Name = name,
        Image = "nginx:latest",
        Port = 80,
        Replicas = 1,
        CpuRequest = "100m",
        CpuLimit = "500m",
        MemoryRequest = "128Mi",
        MemoryLimit = "512Mi"
    };
}
