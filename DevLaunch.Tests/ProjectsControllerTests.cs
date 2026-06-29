using System.Net;
using System.Net.Http.Json;
using DevLaunch.Api;
using DevLaunch.Api.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DevLaunch.Tests;

public class ProjectsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ProjectsControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ── CREATE PROJECT ─────────────────────────────────────────────

    [Fact]
    public async Task CreateProject_ShouldReturnForbiddenOrUnauthorized()
    {
        var request = new CreateProjectRequest
        {
            Name = $"test-project-{Guid.NewGuid()}"
        };

        var response = await _client.PostAsJsonAsync("/api/projects", request);

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Conflict
        );
    }

    // ── LIST PROJECTS ─────────────────────────────────────────────

    [Fact]
    public async Task ListProjects_ShouldReturnOkOrUnauthorized()
    {
        var response = await _client.GetAsync("/api/projects");

        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.Unauthorized
        );
    }

    // ── GET BY ID ─────────────────────────────────────────────

    [Fact]
    public async Task GetProject_InvalidId_ShouldReturnForbiddenOrNotFound()
    {
        var response = await _client.GetAsync($"/api/projects/{Guid.NewGuid()}");

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.Unauthorized
        );
    }

    // ── DELETE PROJECT ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteProject_ShouldReturnExpectedStatus()
    {
        var response = await _client.DeleteAsync($"/api/projects/{Guid.NewGuid()}");

        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent ||
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.Unauthorized
        );
    }
}