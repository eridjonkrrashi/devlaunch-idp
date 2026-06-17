using System.ComponentModel.DataAnnotations;
using DevLaunch.Api.DTOs;

namespace DevLaunch.Tests;

/// <summary>Validates ApplicationSpec constraint rules independently of the API.</summary>
public class ValidationTests
{
    private static List<ValidationResult> Validate(ApplicationSpec spec)
    {
        var results = new List<ValidationResult>();
        var ctx = new ValidationContext(spec);
        Validator.TryValidateObject(spec, ctx, results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("myapp")]
    [InlineData("my-app")]
    [InlineData("a")]
    [InlineData("abc123")]
    public void ValidDnsNames_PassValidation(string name)
    {
        var spec = ValidSpec(name);
        Assert.Empty(Validate(spec));
    }

    [Theory]
    [InlineData("MyApp")]         // uppercase
    [InlineData("-myapp")]        // starts with hyphen
    [InlineData("myapp-")]        // ends with hyphen
    [InlineData("my_app")]        // underscore
    [InlineData("")]              // empty
    public void InvalidDnsNames_FailValidation(string name)
    {
        var spec = ValidSpec(name);
        Assert.NotEmpty(Validate(spec));
    }

    [Fact]
    public void EmptyImage_FailsValidation()
    {
        var spec = ValidSpec("app");
        spec.Image = "";
        Assert.NotEmpty(Validate(spec));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(-1)]
    public void OutOfRangeReplicas_FailValidation(int replicas)
    {
        var spec = ValidSpec("app");
        spec.Replicas = replicas;
        Assert.NotEmpty(Validate(spec));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public void ValidReplicas_PassValidation(int replicas)
    {
        var spec = ValidSpec("app");
        spec.Replicas = replicas;
        Assert.Empty(Validate(spec));
    }

    [Theory]
    [InlineData("100m")]
    [InlineData("1")]
    [InlineData("500m")]
    public void ValidCpuRequests_PassValidation(string cpu)
    {
        var spec = ValidSpec("app");
        spec.CpuRequest = cpu;
        Assert.Empty(Validate(spec));
    }

    [Theory]
    [InlineData("128Mi")]
    [InlineData("1Gi")]
    [InlineData("512M")]
    public void ValidMemoryRequests_PassValidation(string mem)
    {
        var spec = ValidSpec("app");
        spec.MemoryRequest = mem;
        Assert.Empty(Validate(spec));
    }

    [Theory]
    [InlineData("128kb")]
    [InlineData("1TB")]
    public void InvalidMemoryRequests_FailValidation(string mem)
    {
        var spec = ValidSpec("app");
        spec.MemoryRequest = mem;
        Assert.NotEmpty(Validate(spec));
    }

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
