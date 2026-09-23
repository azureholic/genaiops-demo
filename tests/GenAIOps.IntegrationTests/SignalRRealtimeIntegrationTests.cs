using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenAIOps.Application.Realtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.IntegrationTests;

public sealed class SignalRRealtimeIntegrationTests
{
    [Fact]
    public async Task SignalR_negotiate_connect_and_typed_event_delivery_succeed()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage negotiation = await client.PostAsync(
            "/hubs/realtime/negotiate?negotiateVersion=1",
            content: null);
        using JsonDocument negotiationBody = await ReadJsonAsync(negotiation);

        Assert.Equal(HttpStatusCode.OK, negotiation.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(
            negotiationBody.RootElement.GetProperty("connectionToken").GetString()));

        await using HubConnection connection = CreateConnection(factory);
        TaskCompletionSource<MetricsUpdated> metrics = NewCompletion<MetricsUpdated>();
        TaskCompletionSource<EvaluationUpdated> evaluation =
            NewCompletion<EvaluationUpdated>();
        TaskCompletionSource<ExperimentUpdated> experiment =
            NewCompletion<ExperimentUpdated>();
        TaskCompletionSource<ReleaseUpdated> release = NewCompletion<ReleaseUpdated>();
        connection.On<MetricsUpdated>(
            nameof(IRealtimeClient.MetricsUpdated),
            update => metrics.TrySetResult(update));
        connection.On<EvaluationUpdated>(
            nameof(IRealtimeClient.EvaluationUpdated),
            update => evaluation.TrySetResult(update));
        connection.On<ExperimentUpdated>(
            nameof(IRealtimeClient.ExperimentUpdated),
            update => experiment.TrySetResult(update));
        connection.On<ReleaseUpdated>(
            nameof(IRealtimeClient.ReleaseUpdated),
            update => release.TrySetResult(update));

        await connection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, connection.State);

        IRealtimePublisher publisher =
            factory.Services.GetRequiredService<IRealtimePublisher>();
        DateTimeOffset now = DateTimeOffset.Parse("2026-09-23T12:00:00Z");
        await publisher.PublishMetricsAsync(
            new MetricsUpdated(
                "v2",
                now.AddHours(-1),
                now,
                3,
                3,
                0,
                new Dictionary<string, double> { ["taskAdherence"] = 0.94 },
                now));
        await publisher.PublishEvaluationAsync(
            new EvaluationUpdated(
                "v1",
                "v2",
                "completed",
                new Dictionary<string, double> { ["groundedness"] = 0.97 },
                42,
                now));
        await publisher.PublishExperimentAsync(
            new ExperimentUpdated(
                "running",
                [new ExperimentAllocationUpdated("v2", 90), new("v1", 10)],
                now));
        await publisher.PublishReleaseAsync(
            new ReleaseUpdated("promotion", "v2", "promoted", now));

        Assert.Equal("v2", (await metrics.Task.WaitAsync(TimeSpan.FromSeconds(5))).PromptVersion);
        Assert.Equal(
            "completed",
            (await evaluation.Task.WaitAsync(TimeSpan.FromSeconds(5))).Lifecycle);
        Assert.Equal(
            90,
            (await experiment.Task.WaitAsync(TimeSpan.FromSeconds(5))).Allocations[0].Percentage);
        Assert.Equal(
            "promoted",
            (await release.Task.WaitAsync(TimeSpan.FromSeconds(5))).Lifecycle);
    }

    [Fact]
    public async Task Release_transition_publishes_but_rejection_does_not()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        await using HubConnection connection = CreateConnection(factory);
        int releaseCount = 0;
        TaskCompletionSource<ReleaseUpdated> delivered = NewCompletion<ReleaseUpdated>();
        connection.On<ReleaseUpdated>(
            nameof(IRealtimeClient.ReleaseUpdated),
            update =>
            {
                Interlocked.Increment(ref releaseCount);
                delivered.TrySetResult(update);
            });
        await connection.StartAsync();

        PromotionEndpointTests.SeededRegistry promoted = await PromotionEndpointTests.SeedAsync(
            factory,
            "signalr-promote",
            "v2",
            0.94,
            0.97,
            0.95);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage success = await PromotionEndpointTests.PostAsync(
            client,
            "/api/promote/v2",
            "signalr-promote",
            "signalr-success",
            promoted.ETag);
        ReleaseUpdated update = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal("promotion", update.Operation);
        Assert.Equal("promoted", update.Lifecycle);
        Assert.Equal(1, Volatile.Read(ref releaseCount));

        PromotionEndpointTests.SeededRegistry rejected = await PromotionEndpointTests.SeedAsync(
            factory,
            "signalr-reject",
            "v3",
            0.72,
            0.75,
            0.58);
        using HttpResponseMessage failure = await PromotionEndpointTests.PostAsync(
            client,
            "/api/promote/v3",
            "signalr-reject",
            "signalr-failure",
            rejected.ETag);
        await Task.Delay(250);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, failure.StatusCode);
        Assert.Equal(1, Volatile.Read(ref releaseCount));
    }

    [Fact]
    public void Event_contracts_exclude_sensitive_and_personal_fields()
    {
        string[] forbidden =
        [
            "promptText",
            "promptContent",
            "content",
            "message",
            "input",
            "output",
            "secret",
            "key",
            "actor",
            "user",
            "email",
            "agentId",
            "registryId",
            "correlationId",
        ];
        Type[] contracts =
        [
            typeof(MetricsUpdated),
            typeof(EvaluationUpdated),
            typeof(ExperimentUpdated),
            typeof(ExperimentAllocationUpdated),
            typeof(ReleaseUpdated),
        ];

        Assert.All(
            contracts.SelectMany(type => type.GetProperties()),
            property => Assert.DoesNotContain(
                forbidden,
                name => property.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder => builder.UseEnvironment("Development"));

    private static HubConnection CreateConnection(WebApplicationFactory<Program> factory) =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "/hubs/realtime"),
                options =>
                {
                    options.Transports =
                        HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.WebSocketFactory = async (context, cancellationToken) =>
                        await factory.Server.CreateWebSocketClient().ConnectAsync(
                            context.Uri,
                            cancellationToken);
                })
            .Build();

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}
