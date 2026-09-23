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

public sealed class PromotionEndpointTests
{
    [Fact]
    public async Task Promotion_endpoint_promotes_v2_and_replays_idempotently()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        SeededRegistry seeded = await SeedAsync(factory, "promote-api", "v2", 0.94, 0.97, 0.95);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage first = await PostAsync(
            client,
            "/api/promote/v2",
            "promote-api",
            "api-promote-v2",
            seeded.ETag);
        using JsonDocument firstBody = await ReadJsonAsync(first);
        using HttpResponseMessage replay = await PostAsync(
            client,
            "/api/promote/v2",
            "promote-api",
            "api-promote-v2",
            seeded.ETag);
        using JsonDocument replayBody = await ReadJsonAsync(replay);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("v2", firstBody.RootElement.GetProperty("production").GetProperty("promptVersion").GetString());
        Assert.False(firstBody.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(replayBody.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(
            firstBody.RootElement.GetProperty("release").GetProperty("id").GetString(),
            replayBody.RootElement.GetProperty("release").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Promotion_endpoint_rejects_v3_regression_with_gate_evidence()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        SeededRegistry seeded = await SeedAsync(factory, "reject-api", "v3", 0.72, 0.75, 0.58);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await PostAsync(
            client,
            "/api/promote/v3",
            "reject-api",
            "api-reject-v3",
            seeded.ETag);
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            "quality_gate_rejected",
            body.RootElement.GetProperty("code").GetString());
        Assert.False(
            body.RootElement.GetProperty("gateEvidence").GetProperty("passed").GetBoolean());
        Assert.Contains(
            body.RootElement.GetProperty("gateEvidence").GetProperty("reasons").EnumerateArray(),
            reason => reason.GetString()!.Contains("toolAccuracy", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Promotion_endpoint_requires_idempotency_and_etag_headers(
        bool includeIdempotency,
        bool includeETag)
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        SeededRegistry seeded = await SeedAsync(factory, "headers-api", "v2", 0.94, 0.97, 0.95);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            "/api/promote/v2")
        {
            Content = JsonContent.Create(new { actor = "api-test", registryId = "headers-api" }),
        };
        if (includeIdempotency)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", "headers");
        }

        if (includeETag)
        {
            request.Headers.TryAddWithoutValidation("If-Match", seeded.ETag);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_release_command",
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Promotion_endpoint_reports_stale_etag_explicitly()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        await SeedAsync(factory, "stale-api", "v2", 0.94, 0.97, 0.95);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await PostAsync(
            client,
            "/api/promote/v2",
            "stale-api",
            "stale-command",
            "\"stale\"");
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("stale_registry", body.RootElement.GetProperty("code").GetString());
    }

    internal static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder.UseEnvironment("Development"));

    internal static async Task<SeededRegistry> SeedAsync(
        WebApplicationFactory<Program> factory,
        string registryId,
        string candidateVersion,
        double taskAdherence,
        double groundedness,
        double toolAccuracy)
    {
        IAgentRegistryService registry =
            factory.Services.GetRequiredService<IAgentRegistryService>();
        RegistrySnapshot initial =
            await registry.RegisterCandidateAsync(registryId, "agent-v1", "v1");
        RegistrySnapshot production =
            await registry.PromoteCandidateAsync(registryId, "agent-v1", initial.ETag);
        RegistrySnapshot candidate = await registry.RegisterCandidateAsync(
            registryId,
            $"agent-{candidateVersion}",
            candidateVersion,
            production.ETag);
        IRepository<MetricSnapshotRecord> metrics =
            factory.Services.GetRequiredService<IRepository<MetricSnapshotRecord>>();
        await metrics.CreateAsync(
            new MetricSnapshotRecord(
                $"metrics-{registryId}-{candidateVersion}",
                registryId,
                candidateVersion,
                DateTimeOffset.Parse("2026-09-22T00:00:00Z"),
                DateTimeOffset.Parse("2026-09-23T00:00:00Z"),
                DateTimeOffset.Parse("2026-09-23T00:01:00Z"),
                SampleCount: 3,
                SuccessfulCount: 3,
                FailureCount: 0,
                ["one", "two", "three"],
                new Dictionary<string, double>
                {
                    ["taskAdherence"] = taskAdherence,
                    ["groundedness"] = groundedness,
                    ["toolAccuracy"] = toolAccuracy,
                    ["failureRate"] = 0,
                    ["latencyMilliseconds"] = 100,
                    ["sampleCount"] = 3,
                }));
        return new SeededRegistry(candidate.ETag);
    }

    internal static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string uri,
        string registryId,
        string idempotencyKey,
        string eTag)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new { actor = "api-test", registryId }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        return await client.SendAsync(request);
    }

    internal static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    internal sealed record SeededRegistry(string ETag);
}

public sealed class RollbackEndpointTests
{
    [Fact]
    public async Task Rollback_endpoint_restores_v2_after_bad_v3_and_replays()
    {
        await using WebApplicationFactory<Program> factory =
            PromotionEndpointTests.CreateFactory();
        PromotionEndpointTests.SeededRegistry seeded =
            await PromotionEndpointTests.SeedAsync(
                factory,
                "rollback-api",
                "v2",
                0.94,
                0.97,
                0.95);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage promoteV2 = await PromotionEndpointTests.PostAsync(
            client,
            "/api/promote/v2",
            "rollback-api",
            "promote-v2-before-rollback",
            seeded.ETag);
        using JsonDocument promoteBody =
            await PromotionEndpointTests.ReadJsonAsync(promoteV2);
        string promotedETag = promoteBody.RootElement.GetProperty("eTag").GetString()!;
        IAgentRegistryService registry =
            factory.Services.GetRequiredService<IAgentRegistryService>();
        RegistrySnapshot candidateV3 = await registry.RegisterCandidateAsync(
            "rollback-api",
            "agent-v3",
            "v3",
            promotedETag);
        RegistrySnapshot badV3 = await registry.PromoteCandidateAsync(
            "rollback-api",
            "agent-v3",
            candidateV3.ETag);

        using HttpResponseMessage rollback = await PromotionEndpointTests.PostAsync(
            client,
            "/api/rollback",
            "rollback-api",
            "rollback-bad-v3",
            badV3.ETag);
        using JsonDocument body = await PromotionEndpointTests.ReadJsonAsync(rollback);
        using HttpResponseMessage replay = await PromotionEndpointTests.PostAsync(
            client,
            "/api/rollback",
            "rollback-api",
            "rollback-bad-v3",
            badV3.ETag);
        using JsonDocument replayBody =
            await PromotionEndpointTests.ReadJsonAsync(replay);

        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal(
            "v2",
            body.RootElement.GetProperty("production").GetProperty("promptVersion").GetString());
        Assert.Equal(
            "v2",
            body.RootElement.GetProperty("release").GetProperty("rollbackTarget")
                .GetProperty("promptVersion").GetString());
        Assert.True(replayBody.RootElement.GetProperty("replayed").GetBoolean());
    }
}
