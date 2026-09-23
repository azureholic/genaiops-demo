using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Domain.Records;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.IntegrationTests;

public sealed class VersionsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public VersionsEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Versions_endpoint_returns_assignments_and_version_metadata()
    {
        using HttpClient client = factory.CreateClient();
        IAgentRegistryService registry = factory.Services.GetRequiredService<IAgentRegistryService>();
        IRepository<PromptVersionRecord> versions =
            factory.Services.GetRequiredService<IRepository<PromptVersionRecord>>();
        RegistrySnapshot candidate =
            await registry.RegisterCandidateAsync("versions-test", "foundry-v1", "v1");
        await registry.PromoteCandidateAsync("versions-test", "foundry-v1", candidate.ETag);
        await versions.CreateAsync(
            new PromptVersionRecord(
                "v1",
                "versions-test",
                "v1",
                "Production prompt",
                "sha256:abc",
                PromptVersionLifecycle.Production,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                new Dictionary<string, double> { ["groundedness"] = 0.97 }));

        using HttpResponseMessage response =
            await client.GetAsync("/api/versions?registryId=versions-test&pageSize=10");
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("foundry-v1", body.RootElement.GetProperty("production").GetProperty("agentId").GetString());
        Assert.Equal("v1", body.RootElement.GetProperty("versions")[0].GetProperty("version").GetString());
        Assert.Equal("Production", body.RootElement.GetProperty("versions")[0].GetProperty("lifecycle").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("eTag").GetString()));
    }

    [Fact]
    public async Task Versions_endpoint_returns_explicit_not_found_problem()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/api/versions?registryId=does-not-exist");
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Registry not found", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Versions_endpoint_returns_explicit_error_for_invalid_page_size()
    {
        using HttpClient client = factory.CreateClient();
        IAgentRegistryService registry = factory.Services.GetRequiredService<IAgentRegistryService>();
        await registry.RegisterCandidateAsync("invalid-page-test", "foundry-v1", "v1");

        using HttpResponseMessage response =
            await client.GetAsync("/api/versions?registryId=invalid-page-test&pageSize=1000");
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid request", body.RootElement.GetProperty("title").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
