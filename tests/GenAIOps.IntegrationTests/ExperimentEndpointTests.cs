using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Domain.Records;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.IntegrationTests;

public sealed class ExperimentEndpointTests
{
    [Fact]
    public async Task AbTest_start_update_end_controls_chat_routing_and_metadata()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        await SeedApprovedVersionsAsync(factory, "ab-api");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage started = await PostAsync(
            client,
            new
            {
                action = "start",
                actor = "api-test",
                registryId = "ab-api",
                name = "v2-v1",
                allocations = new[]
                {
                    new { promptVersion = "v2", percentage = 90 },
                    new { promptVersion = "v1", percentage = 10 },
                },
            });
        using JsonDocument startBody = await ReadJsonAsync(started);
        string startETag = startBody.RootElement.GetProperty("eTag").GetString()!;

        using HttpRequestMessage chatRequest = new(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { message = "experiment", registryId = "ab-api" }),
        };
        chatRequest.Headers.Add("X-Assignment-Key", "session-123");
        chatRequest.Headers.Add("X-Correlation-ID", "ab-chat");
        using HttpResponseMessage chat = await client.SendAsync(chatRequest);
        using JsonDocument chatBody = await ReadJsonAsync(chat);

        using HttpResponseMessage updated = await PostAsync(
            client,
            new
            {
                action = "update",
                actor = "api-test",
                registryId = "ab-api",
                allocations = new[]
                {
                    new { promptVersion = "v2", percentage = 80 },
                    new { promptVersion = "v1", percentage = 20 },
                },
            },
            startETag);
        using JsonDocument updateBody = await ReadJsonAsync(updated);
        string updateETag = updateBody.RootElement.GetProperty("eTag").GetString()!;
        using HttpResponseMessage ended = await PostAsync(
            client,
            new { action = "end", actor = "api-test", registryId = "ab-api" },
            updateETag);
        using JsonDocument endBody = await ReadJsonAsync(ended);

        using HttpResponseMessage normalChat = await client.PostAsJsonAsync(
            "/api/chat",
            new { message = "normal", registryId = "ab-api" });
        using JsonDocument normalBody = await ReadJsonAsync(normalChat);
        IRepository<ChatRequestMetadataRecord> metadata =
            factory.Services.GetRequiredService<IRepository<ChatRequestMetadataRecord>>();
        ChatRequestMetadataRecord stored = Assert.Single(
            (await metadata.QueryAsync(new RecordQuery("ab-api", "chatRequestMetadata"))).Items,
            item => item.Value.CorrelationId == "ab-chat").Value;

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        Assert.NotNull(chatBody.RootElement.GetProperty("experimentId").GetString());
        Assert.Equal(
            chatBody.RootElement.GetProperty("assignedPromptVersion").GetString(),
            stored.AssignedPromptVersion);
        Assert.Equal(
            chatBody.RootElement.GetProperty("experimentId").GetString(),
            stored.ExperimentId);
        Assert.DoesNotContain("session-123", JsonSerializer.Serialize(stored), StringComparison.Ordinal);
        Assert.Equal(
            "Completed",
            endBody.RootElement.GetProperty("experiment").GetProperty("lifecycle").GetString());
        Assert.Equal(HttpStatusCode.OK, normalChat.StatusCode);
        Assert.Equal("v2", normalBody.RootElement.GetProperty("assignedPromptVersion").GetString());
        Assert.Equal(JsonValueKind.Null, normalBody.RootElement.GetProperty("experimentId").ValueKind);
    }

    [Fact]
    public async Task AbTest_rejects_bad_allocation_ineligible_version_and_stale_update()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        await SeedApprovedVersionsAsync(factory, "ab-errors");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage invalid = await PostAsync(
            client,
            new
            {
                action = "start",
                actor = "api-test",
                registryId = "ab-errors",
                name = "bad-total",
                allocations = new[]
                {
                    new { promptVersion = "v2", percentage = 90 },
                    new { promptVersion = "v1", percentage = 9 },
                },
            });
        using JsonDocument invalidBody = await ReadJsonAsync(invalid);

        using HttpResponseMessage ineligible = await PostAsync(
            client,
            new
            {
                action = "start",
                actor = "api-test",
                registryId = "ab-errors",
                name = "unknown",
                allocations = new[]
                {
                    new { promptVersion = "v2", percentage = 90 },
                    new { promptVersion = "v9", percentage = 10 },
                },
            });
        using JsonDocument ineligibleBody = await ReadJsonAsync(ineligible);

        using HttpResponseMessage started = await PostAsync(
            client,
            new
            {
                action = "start",
                actor = "api-test",
                registryId = "ab-errors",
                name = "valid",
                allocations = new[]
                {
                    new { promptVersion = "v2", percentage = 90 },
                    new { promptVersion = "v1", percentage = 10 },
                },
            });
        using HttpResponseMessage stale = await PostAsync(
            client,
            new
            {
                action = "update",
                actor = "api-test",
                registryId = "ab-errors",
                allocations = new[]
                {
                    new { promptVersion = "v2", percentage = 80 },
                    new { promptVersion = "v1", percentage = 20 },
                },
            },
            "\"stale\"");
        using JsonDocument staleBody = await ReadJsonAsync(stale);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_experiment", invalidBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ineligible.StatusCode);
        Assert.Equal("ineligible_version", ineligibleBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("stale_experiment", staleBody.RootElement.GetProperty("code").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder.UseEnvironment("Development"));

    private static async Task SeedApprovedVersionsAsync(
        WebApplicationFactory<Program> factory,
        string registryId)
    {
        IAgentRegistryService registry =
            factory.Services.GetRequiredService<IAgentRegistryService>();
        RegistrySnapshot v1 =
            await registry.RegisterCandidateAsync(registryId, "agent-v1", "v1");
        RegistrySnapshot productionV1 =
            await registry.PromoteCandidateAsync(registryId, "agent-v1", v1.ETag);
        RegistrySnapshot v2 = await registry.RegisterCandidateAsync(
            registryId,
            "agent-v2",
            "v2",
            productionV1.ETag);
        await registry.PromoteCandidateAsync(registryId, "agent-v2", v2.ETag);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        object body,
        string? eTag = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/abtest")
        {
            Content = JsonContent.Create(body),
        };
        if (eTag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", eTag);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}
