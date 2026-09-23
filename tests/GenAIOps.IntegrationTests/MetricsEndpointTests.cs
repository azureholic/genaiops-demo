using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenAIOps.IntegrationTests;

public sealed class MetricsEndpointTests
{
    [Fact]
    public async Task Metrics_endpoint_returns_deterministic_continuous_evaluation_baselines()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        JsonElement[] snapshots = await WaitForSnapshotsAsync(client);
        JsonElement v2 = Assert.Single(
            snapshots,
            snapshot => snapshot.GetProperty("promptVersion").GetString() == "v2");

        Assert.Equal(3, v2.GetProperty("sampleCount").GetInt32());
        Assert.Equal(3, v2.GetProperty("successfulCount").GetInt32());
        Assert.Equal(0, v2.GetProperty("failureCount").GetInt32());
        Assert.Equal(
            0.94,
            v2.GetProperty("metrics").GetProperty("taskAdherence").GetDouble(),
            precision: 10);
        Assert.Equal(
            0.97,
            v2.GetProperty("metrics").GetProperty("groundedness").GetDouble(),
            precision: 10);
        Assert.Equal(
            0.95,
            v2.GetProperty("metrics").GetProperty("toolAccuracy").GetDouble(),
            precision: 10);
        Assert.True(
            v2.GetProperty("windowEnd").GetDateTimeOffset()
            > v2.GetProperty("windowStart").GetDateTimeOffset());
    }

    [Fact]
    public async Task Metrics_endpoint_filters_version_and_paginates()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        await WaitForSnapshotsAsync(client);

        using HttpResponseMessage response =
            await client.GetAsync("/api/metrics?version=v1&pageSize=1");
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement snapshot = Assert.Single(
            body.RootElement.GetProperty("snapshots").EnumerateArray());
        Assert.Equal("v1", snapshot.GetProperty("promptVersion").GetString());
        Assert.Null(body.RootElement.GetProperty("continuationToken").GetString());
    }

    [Theory]
    [InlineData("/api/metrics?pageSize=0")]
    [InlineData("/api/metrics?pageSize=101")]
    [InlineData("/api/metrics?continuationToken=invalid")]
    [InlineData("/api/metrics?from=2026-09-24T00:00:00Z&to=2026-09-23T00:00:00Z")]
    public async Task Metrics_endpoint_returns_bad_request_for_invalid_filters(string uri)
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(uri);
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid request", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Metrics_endpoint_returns_empty_for_unknown_version()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        await WaitForSnapshotsAsync(client);

        using HttpResponseMessage response =
            await client.GetAsync("/api/metrics?version=v99");
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body.RootElement.GetProperty("snapshots").EnumerateArray());
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder.UseEnvironment("Development"));

    private static async Task<JsonElement[]> WaitForSnapshotsAsync(HttpClient client)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            using HttpResponseMessage response = await client.GetAsync("/api/metrics");
            using JsonDocument body = await ReadJsonAsync(response);
            JsonElement[] snapshots = body.RootElement.GetProperty("snapshots")
                .EnumerateArray()
                .Select(item => item.Clone())
                .ToArray();
            if (snapshots.Length >= 3)
            {
                return snapshots;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Continuous evaluation metrics were not generated.");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
