using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using GenAIOps.Application.Chat;
using GenAIOps.Application.Metrics;
using GenAIOps.Application.Observability;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Application.Releases;
using GenAIOps.Application.Shadow;
using GenAIOps.Domain.Records;
using GenAIOps.Infrastructure.Chat;
using GenAIOps.Infrastructure.Metrics;
using GenAIOps.Infrastructure.Observability;
using GenAIOps.Infrastructure.Persistence;
using GenAIOps.Infrastructure.Shadow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.UnitTests;

[Collection(nameof(ObservabilityCollection))]
public sealed class ObservabilityTests
{
    [Fact]
    public async Task Durable_shadow_work_preserves_trace_correlation_through_aggregation()
    {
        using ActivityCapture capture = new();
        InMemoryShadowWorkQueue queue = new();
        InMemoryRepository<ShadowEvaluationRecord> evaluations = new();
        ChatService chat = await CreateChatServiceAsync(new FakeChatGateway(), queue);
        using Activity incoming = new Activity("incoming")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        await chat.SendAsync(
            "sensitive-registry",
            "sensitive prompt text",
            "sensitive-correlation");
        ShadowWorkDelivery delivery = await queue.ReceiveAsync();
        ShadowEvaluationProcessor processor = new(
            new FakeChatGateway(),
            new FakeResponseEvaluator(),
            evaluations,
            ShadowProcessingOptions.Default);
        ShadowProcessingResult result = await processor.ProcessAsync(delivery);
        MetricsAggregator aggregator = new(
            evaluations,
            new InMemoryRepository<MetricSnapshotRecord>());
        await aggregator.AggregateAsync(
            new AggregationWindow(
                "sensitive-registry",
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(5)));
        incoming.Stop();

        Assert.Equal(ShadowProcessingDisposition.Completed, result.Disposition);
        Activity chatActivity = capture.Single("chat.send");
        Activity shadowActivity = capture.Single("shadow.execute");
        Activity evaluationActivity = capture.Single("shadow.evaluate");
        Activity aggregationActivity = capture.Single("metrics.aggregate");
        Assert.Equal(chatActivity.TraceId, shadowActivity.TraceId);
        Assert.Equal(chatActivity.TraceId, evaluationActivity.TraceId);
        Assert.Equal(chatActivity.TraceId, aggregationActivity.TraceId);
        Assert.Equal(chatActivity.SpanId, shadowActivity.ParentSpanId);
        Assert.Equal(shadowActivity.SpanId, evaluationActivity.ParentSpanId);
        Assert.False(string.IsNullOrWhiteSpace(delivery.Work.TraceParent));
    }

    [Fact]
    public async Task Activities_and_metrics_use_allowlisted_tags_and_accurate_outcomes()
    {
        using ActivityCapture activities = new();
        using MetricCapture metrics = new();
        const string secretPrompt = "prompt-secret-892";
        const string secretCorrelation = "correlation-secret-541";
        ChatService success = await CreateChatServiceAsync(new FakeChatGateway());
        ChatService failure = await CreateChatServiceAsync(new FailingGateway());

        await success.SendAsync("registry-secret-113", secretPrompt, secretCorrelation);
        await Assert.ThrowsAsync<ChatProviderException>(
            () => failure.SendAsync(
                "registry-secret-113",
                secretPrompt,
                secretCorrelation));

        Activity[] chatActivities = activities.ByName("chat.send").ToArray();
        Assert.Equal(2, chatActivities.Length);
        Assert.Contains(
            chatActivities,
            activity => Tag(activity, GenAIOpsTelemetry.OutcomeTag) == "success"
                && activity.Status == ActivityStatusCode.Ok);
        Assert.Contains(
            chatActivities,
            activity => Tag(activity, GenAIOpsTelemetry.OutcomeTag) == "failure"
                && activity.Status == ActivityStatusCode.Error);
        Assert.All(
            activities.All,
            activity =>
            {
                Assert.All(
                    activity.TagObjects,
                    tag => Assert.Contains(tag.Key, GenAIOpsTelemetry.AllowedTags));
                string serialized = string.Join(
                    '|',
                    activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));
                Assert.DoesNotContain(secretPrompt, serialized, StringComparison.Ordinal);
                Assert.DoesNotContain(secretCorrelation, serialized, StringComparison.Ordinal);
                Assert.DoesNotContain("registry-secret-113", serialized, StringComparison.Ordinal);
            });

        MetricMeasurement[] operations = metrics.Measurements
            .Where(item => item.InstrumentName == "genaiops.operations")
            .Where(item => item.Tags.GetValueOrDefault(GenAIOpsTelemetry.OperationTag) == "chat")
            .ToArray();
        Assert.Contains(
            operations,
            item => item.Tags.GetValueOrDefault(GenAIOpsTelemetry.OutcomeTag) == "success"
                && item.Value == 1);
        Assert.Contains(
            operations,
            item => item.Tags.GetValueOrDefault(GenAIOpsTelemetry.OutcomeTag) == "failure"
                && item.Value == 1);
        Assert.All(
            metrics.Measurements,
            measurement => Assert.All(
                measurement.Tags.Keys,
                tag => Assert.Contains(tag, GenAIOpsTelemetry.AllowedTags)));
    }

    [Fact]
    public void Azure_monitor_export_requires_connection_string_only_when_enabled()
    {
        ConfigurationBuilder configurationBuilder = new();
        IConfiguration disabled = configurationBuilder
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Observability:AzureMonitor:Enabled"] = "false",
                })
            .Build();
        ServiceCollection services = new();
        services.AddGenAIOpsObservability(disabled, "test-service");

        IConfiguration enabledWithoutConnectionString = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Observability:AzureMonitor:Enabled"] = "true",
                })
            .Build();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddGenAIOpsObservability(
                enabledWithoutConnectionString,
                "test-service"));

        Assert.Contains(
            "Observability:AzureMonitor:ConnectionString is required",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_promotion_is_never_reported_as_success()
    {
        using ActivityCapture capture = new();
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync("v3", 0.72, 0.75, 0.58);

        await Assert.ThrowsAsync<QualityGateRejectedException>(
            () => fixture.Service.PromoteAsync(
                "v3",
                fixture.Command("sensitive-idempotency-key")));

        Activity promotion = capture.Single("release.promote");
        Activity gate = capture.Single("quality-gate.evaluate");
        Assert.Equal("rejected", Tag(promotion, GenAIOpsTelemetry.OutcomeTag));
        Assert.Equal("failed", Tag(promotion, GenAIOpsTelemetry.GateResultTag));
        Assert.Equal(ActivityStatusCode.Error, promotion.Status);
        Assert.Equal("rejected", Tag(gate, GenAIOpsTelemetry.OutcomeTag));
        Assert.DoesNotContain(
            promotion.TagObjects,
            tag => string.Equals(
                tag.Value?.ToString(),
                "sensitive-idempotency-key",
                StringComparison.Ordinal));
    }

    private static string? Tag(Activity activity, string name) =>
        activity.TagObjects.FirstOrDefault(tag => tag.Key == name).Value?.ToString();

    private static async Task<ChatService> CreateChatServiceAsync(
        IChatGateway gateway,
        IShadowWorkPublisher? publisher = null)
    {
        AgentRegistryService registry =
            new(new InMemoryRepository<GenAIOps.Domain.Registry.AgentRegistryState>());
        RegistrySnapshot initial =
            await registry.RegisterCandidateAsync("registry-secret-113", "agent-secret", "v1");
        await registry.PromoteCandidateAsync(
            "registry-secret-113",
            "agent-secret",
            initial.ETag);
        RegistrySnapshot otherInitial =
            await registry.RegisterCandidateAsync(
                "sensitive-registry",
                "agent-secret",
                "v1");
        RegistrySnapshot production = await registry.PromoteCandidateAsync(
            "sensitive-registry",
            "agent-secret",
            otherInitial.ETag);
        await registry.RegisterCandidateAsync(
            "sensitive-registry",
            "candidate-secret",
            "v2",
            production.ETag);
        return new ChatService(
            registry,
            gateway,
            new InMemoryRepository<ChatRequestMetadataRecord>(),
            publisher);
    }

    private sealed class FailingGateway : IChatGateway
    {
        public Task<ChatGatewayResponse> SendAsync(
            ChatGatewayRequest request,
            CancellationToken cancellationToken = default) =>
            throw new ChatProviderException("Secret provider payload.");
    }

    private sealed class ActivityCapture : IDisposable
    {
        private readonly ConcurrentBag<Activity> stopped = [];
        private readonly ActivityListener listener;

        public ActivityCapture()
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source =>
                    source.Name == GenAIOpsTelemetry.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped.Add,
            };
            ActivitySource.AddActivityListener(listener);
        }

        public IEnumerable<Activity> All => stopped;

        public IEnumerable<Activity> ByName(string name) =>
            stopped.Where(activity => activity.OperationName == name);

        public Activity Single(string name) => Assert.Single(ByName(name));

        public void Dispose() => listener.Dispose();
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly ConcurrentBag<MetricMeasurement> measurements = [];

        public MetricCapture()
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == GenAIOpsTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) =>
                    measurements.Add(Create(instrument, value, tags)));
            listener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) =>
                    measurements.Add(Create(instrument, value, tags)));
            listener.Start();
        }

        public IEnumerable<MetricMeasurement> Measurements => measurements;

        public void Dispose() => listener.Dispose();

        private static MetricMeasurement Create<T>(
            Instrument instrument,
            T value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : struct, IConvertible =>
            new(
                instrument.Name,
                Convert.ToDouble(value),
                tags.ToArray().ToDictionary(
                    tag => tag.Key,
                    tag => tag.Value?.ToString(),
                    StringComparer.Ordinal));
    }

    private sealed record MetricMeasurement(
        string InstrumentName,
        double Value,
        IReadOnlyDictionary<string, string?> Tags);
}

[CollectionDefinition(nameof(ObservabilityCollection), DisableParallelization = true)]
public sealed class ObservabilityCollection;
