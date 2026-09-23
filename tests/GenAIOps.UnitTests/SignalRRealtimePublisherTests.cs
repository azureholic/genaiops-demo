using GenAIOps.Application.Metrics;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Realtime;
using GenAIOps.Domain.Records;
using GenAIOps.Infrastructure.Persistence;
using GenAIOps.Infrastructure.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.UnitTests;

public sealed class SignalRRealtimePublisherTests
{
    [Fact]
    public async Task Metrics_publish_only_after_new_snapshot_is_persisted()
    {
        InMemoryRepository<ShadowEvaluationRecord> evaluations = new();
        InMemoryRepository<MetricSnapshotRecord> snapshots = new();
        RecordingPublisher publisher = new();
        MetricsAggregator aggregator = new(evaluations, snapshots, publisher);
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-23T10:00:00Z");
        await evaluations.CreateAsync(
            new ShadowEvaluationRecord(
                "evaluation",
                "default",
                "evaluation",
                "default",
                "production-agent",
                "v1",
                "candidate-agent",
                "v2",
                "sensitive prompt",
                "sensitive production output",
                "sensitive candidate output",
                EvaluationLifecycle.Completed,
                new Dictionary<string, double>
                {
                    ["taskAdherence"] = 0.94,
                    ["groundedness"] = 0.97,
                    ["toolAccuracy"] = 0.95,
                },
                50,
                1,
                null,
                null,
                start,
                start.AddMinutes(1)));

        await aggregator.AggregateAsync(
            new AggregationWindow("default", start, start.AddHours(1)));
        await aggregator.AggregateAsync(
            new AggregationWindow("default", start, start.AddHours(1)));

        MetricsUpdated update = Assert.Single(publisher.Metrics);
        Assert.Equal("v2", update.PromptVersion);
        Assert.Equal(1, update.SampleCount);
        string payload = System.Text.Json.JsonSerializer.Serialize(update);
        Assert.DoesNotContain("sensitive", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Worker_registration_resolves_noop_without_a_hub()
    {
        ServiceCollection services = new();
        services.AddNoOpRealtimeUpdates();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<NoOpRealtimePublisher>(
            provider.GetRequiredService<IRealtimePublisher>());
    }

    private sealed class RecordingPublisher : IRealtimePublisher
    {
        public List<MetricsUpdated> Metrics { get; } = [];

        public Task PublishMetricsAsync(
            MetricsUpdated update,
            CancellationToken cancellationToken = default)
        {
            Metrics.Add(update);
            return Task.CompletedTask;
        }

        public Task PublishEvaluationAsync(
            EvaluationUpdated update,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublishExperimentAsync(
            ExperimentUpdated update,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublishReleaseAsync(
            ReleaseUpdated update,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
