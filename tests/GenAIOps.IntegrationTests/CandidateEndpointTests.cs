using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenAIOps.IntegrationTests;

public sealed class CandidateEndpointTests
{
    [Fact]
    public async Task RegisterCandidateCreatesAndReplacesCandidate()
    {
        await using WebApplicationFactory<Program> factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/candidates/v2",
            new { agentId = "support-v2", actor = "ci", registryId = "candidate-test" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        JsonElement first = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("support-v2", first.GetProperty("state").GetProperty("candidate").GetProperty("agentId").GetString());
        Assert.Equal("v2", first.GetProperty("state").GetProperty("candidate").GetProperty("promptVersion").GetString());

        using HttpResponseMessage replaced = await client.PostAsJsonAsync(
            "/api/candidates/v3",
            new { agentId = "support-v3", actor = "ci", registryId = "candidate-test" });

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        JsonElement second = await replaced.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("support-v3", second.GetProperty("state").GetProperty("candidate").GetProperty("agentId").GetString());
        Assert.Equal("v3", second.GetProperty("state").GetProperty("candidate").GetProperty("promptVersion").GetString());
    }
}
