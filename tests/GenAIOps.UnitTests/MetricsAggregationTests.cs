using GenAIOps.Application.Metrics;
using GenAIOps.Application.Persistence;
using GenAIOps.Domain.Records;
using GenAIOps.Infrastructure.Metrics;
using GenAIOps.Infrastructure.Persistence;

namespace GenAIOps.UnitTests;

public sealed class MetricsAggregationTests
{
    private static readonly DateTimeOffset WindowStart =
        DateTimeOffset.Parse("2026-09-23T10:00:00Z");
    private static readonly DateTimeOffset WindowEnd =
        DateTimeOffset.Parse("2026-09-23T11:00:00Z");

    [Fact]
    public async Task Empty_window_creates_no_snapshot()
    {
        (MetricsAggregator aggregator, _, InMemoryRepository<MetricSnapshotRecord> snapshots) =
            CreateAggregator();

        IReadOnlyList<MetricSnapshotRecord> result =
            await aggregator.AggregateAsync(Window());
        RepositoryPage<MetricSnapshotRecord> stored =
            await snapshots.QueryAsync(new RecordQuery("default", "metricSnapshot"));

        Assert.Empty(result);
        Assert.Empty(stored.Items);
    }

    [Fact]
    public async Task Aggregation_computes_quality_latency_failure_and_sample_count()
    {
        (MetricsAggregator aggregator, InMemoryRepository<ShadowEvaluationRecord> evaluations, _) =
            CreateAggregator();
        await evaluations.CreateAsync(
            Evaluation("one", "v2", 0.8, 0.9, 1, latency: 100));
        await evaluations.CreateAsync(
            Evaluation("two", "v2", 1, 0.7, 0.5, latency: 300));
        await evaluations.CreateAsync(
            Evaluation(
                "failed",
                "v2",
                lifecycle: EvaluationLifecycle.Poisoned,
                latency: 50));

        MetricSnapshotRecord snapshot =
            Assert.Single(await aggregator.AggregateAsync(Window()));

        Assert.Equal(3, snapshot.SampleCount);
        Assert.Equal(2, snapshot.SuccessfulCount);
        Assert.Equal(1, snapshot.FailureCount);
        Assert.Equal(0.9, snapshot.Metrics["taskAdherence"], precision: 10);
        Assert.Equal(0.8, snapshot.Metrics["groundedness"], precision: 10);
        Assert.Equal(0.75, snapshot.Metrics["toolAccuracy"], precision: 10);
        Assert.Equal(150, snapshot.Metrics["latencyMilliseconds"]);
        Assert.Equal(1d / 3d, snapshot.Metrics["failureRate"], precision: 10);
        Assert.Equal(3, snapshot.Metrics["sampleCount"]);
    }

    [Fact]
    public async Task Replay_is_idempotent_and_duplicate_evaluation_ids_are_not_counted_twice()
    {
        (MetricsAggregator aggregator, InMemoryRepository<ShadowEvaluationRecord> evaluations,
            InMemoryRepository<MetricSnapshotRecord> snapshots) = CreateAggregator();
        await evaluations.CreateAsync(Evaluation("one", "v1", 0.8, 0.9, 0.7));

        MetricSnapshotRecord first =
            Assert.Single(await aggregator.AggregateAsync(Window()));
        MetricSnapshotRecord replay =
            Assert.Single(await aggregator.AggregateAsync(Window()));
        RepositoryPage<MetricSnapshotRecord> stored =
            await snapshots.QueryAsync(new RecordQuery("default", "metricSnapshot"));

        Assert.Equal(first.Id, replay.Id);
        Assert.Single(stored.Items);
        Assert.Equal(1, first.SampleCount);
    }

    [Fact]
    public async Task Late_event_creates_immutable_revision_without_mutating_prior_snapshot()
    {
        (MetricsAggregator aggregator, InMemoryRepository<ShadowEvaluationRecord> evaluations,
            InMemoryRepository<MetricSnapshotRecord> snapshots) = CreateAggregator();
        await evaluations.CreateAsync(Evaluation("one", "v1", 0.8, 0.9, 0.7));
        MetricSnapshotRecord original =
            Assert.Single(await aggregator.AggregateAsync(Window()));
        await evaluations.CreateAsync(Evaluation("late", "v1", 1, 1, 1));

        MetricSnapshotRecord revision =
            Assert.Single(await aggregator.AggregateAsync(Window()));
        RepositoryPage<MetricSnapshotRecord> stored =
            await snapshots.QueryAsync(new RecordQuery("default", "metricSnapshot"));

        Assert.NotEqual(original.Id, revision.Id);
        Assert.Equal(1, original.SampleCount);
        Assert.Equal(2, revision.SampleCount);
        Assert.Equal(2, stored.Items.Count);
        Assert.Equal(0.8, original.Metrics["taskAdherence"]);
    }

    [Fact]
    public void Weighted_aggregation_uses_sample_counts_not_mean_of_means()
    {
        MetricSnapshotRecord small = Snapshot("small", 1, 1, 0, 1);
        MetricSnapshotRecord large = Snapshot(
            "large",
            9,
            9,
            0,
            0,
            windowStart: WindowStart.AddHours(1));

        IReadOnlyDictionary<string, double> combined =
            MetricsAggregator.CombineWeighted([small, large]);

        Assert.Equal(0.1, combined["taskAdherence"], precision: 10);
        Assert.Equal(10, combined["sampleCount"]);
    }

    [Fact]
    public void Weighted_aggregation_uses_only_latest_late_event_revision()
    {
        MetricSnapshotRecord original = Snapshot("original", 1, 1, 0, 0.5);
        MetricSnapshotRecord revision = Snapshot("revision", 2, 2, 0, 0.75);

        IReadOnlyDictionary<string, double> combined =
            MetricsAggregator.CombineWeighted([original, revision]);

        Assert.Equal(2, combined["sampleCount"]);
        Assert.Equal(0.75, combined["taskAdherence"]);
    }

    [Fact]
    public async Task Deterministic_continuous_evaluation_reproduces_fixture_baselines_and_replays()
    {
        string dataset = FindRepositoryPath("EvaluationData");
        InMemoryRepository<ShadowEvaluationRecord> evaluations = new();
        ContinuousEvaluationRunner runner = new(
            new FakeContinuousEvaluationProvider(),
            evaluations,
            new ContinuousEvaluationOptions(
                dataset,
                "default",
                TimeSpan.FromHours(1),
                TimeSpan.FromDays(1)));

        int created = await runner.RunAsync("default", WindowStart);
        int replayed = await runner.RunAsync("default", WindowStart);
        int nextSchedule = await runner.RunAsync("default", WindowStart.AddHours(1));
        RepositoryPage<ShadowEvaluationRecord> page = await evaluations.QueryAsync(
            new RecordQuery("default", "shadowEvaluation"));

        Assert.Equal(9, created);
        Assert.Equal(0, replayed);
        Assert.Equal(9, nextSchedule);
        Assert.Equal(18, page.Items.Count);
        AssertBaseline(page.Items.Select(item => item.Value), "v1", 0.82, 0.89, 0.84, 6);
        AssertBaseline(page.Items.Select(item => item.Value), "v2", 0.94, 0.97, 0.95, 6);
        AssertBaseline(page.Items.Select(item => item.Value), "v3", 0.72, 0.75, 0.58, 6);
    }

    [Fact]
    public async Task Continuous_evaluation_retry_resumes_without_duplicating_completed_fixtures()
    {
        string dataset = FindRepositoryPath("EvaluationData");
        InMemoryRepository<ShadowEvaluationRecord> evaluations = new();
        FlakyContinuousProvider provider = new();
        ContinuousEvaluationRunner runner = new(
            provider,
            evaluations,
            new ContinuousEvaluationOptions(
                dataset,
                "default",
                TimeSpan.FromHours(1),
                TimeSpan.FromDays(1)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("default", WindowStart));
        int createdOnRetry = await runner.RunAsync("default", WindowStart);
        RepositoryPage<ShadowEvaluationRecord> page = await evaluations.QueryAsync(
            new RecordQuery("default", "shadowEvaluation"));

        Assert.Equal(8, createdOnRetry);
        Assert.Equal(9, page.Items.Count);
        Assert.Equal(10, provider.CallCount);
    }

    [Fact]
    public async Task Metrics_query_validates_filters_and_paginates_filtered_results()
    {
        InMemoryRepository<MetricSnapshotRecord> repository = new();
        await repository.CreateAsync(Snapshot("v1-a", 2, 2, 0, 0.8, "v1"));
        await repository.CreateAsync(
            Snapshot("v1-b", 2, 2, 0, 0.9, "v1", WindowStart.AddHours(1)));
        await repository.CreateAsync(Snapshot("v2-a", 2, 2, 0, 1, "v2"));
        MetricsQueryService service = new(repository);

        MetricsPage first = await service.QueryAsync(
            new MetricsQuery("default", "v1", null, null, 1, null));
        MetricsPage second = await service.QueryAsync(
            new MetricsQuery("default", "v1", null, null, 1, first.ContinuationToken));

        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        Assert.Null(second.ContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.QueryAsync(
                new MetricsQuery("default", null, WindowEnd, WindowStart, 10, null)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.QueryAsync(
                new MetricsQuery("default", null, null, null, 10, "not-base64")));
    }

    private static (
        MetricsAggregator Aggregator,
        InMemoryRepository<ShadowEvaluationRecord> Evaluations,
        InMemoryRepository<MetricSnapshotRecord> Snapshots) CreateAggregator()
    {
        InMemoryRepository<ShadowEvaluationRecord> evaluations = new();
        InMemoryRepository<MetricSnapshotRecord> snapshots = new();
        return (new MetricsAggregator(evaluations, snapshots), evaluations, snapshots);
    }

    private static AggregationWindow Window() =>
        new("default", WindowStart, WindowEnd);

    private static ShadowEvaluationRecord Evaluation(
        string id,
        string version,
        double taskAdherence = 0,
        double groundedness = 0,
        double toolAccuracy = 0,
        EvaluationLifecycle lifecycle = EvaluationLifecycle.Completed,
        long latency = 100) =>
        new(
            id,
            "default",
            id,
            "default",
            "production",
            "v1",
            $"candidate-{version}",
            version,
            "input",
            "production",
            lifecycle == EvaluationLifecycle.Completed ? "candidate" : null,
            lifecycle,
            lifecycle == EvaluationLifecycle.Completed
                ? new Dictionary<string, double>
                {
                    ["taskAdherence"] = taskAdherence,
                    ["groundedness"] = groundedness,
                    ["toolAccuracy"] = toolAccuracy,
                }
                : new Dictionary<string, double>(),
            latency,
            AttemptCount: 1,
            ErrorCode: lifecycle == EvaluationLifecycle.Completed ? null : "failed",
            ErrorMessage: null,
            CreatedAt: WindowStart.AddMinutes(10),
            CompletedAt: WindowStart.AddMinutes(11));

    private static MetricSnapshotRecord Snapshot(
        string id,
        int samples,
        int successful,
        int failed,
        double score,
        string version = "v1",
        DateTimeOffset? windowStart = null) =>
        new(
            id,
            "default",
            version,
            windowStart ?? WindowStart,
            (windowStart ?? WindowStart).AddHours(1),
            (windowStart ?? WindowStart).AddHours(1),
            samples,
            successful,
            failed,
            [id],
            new Dictionary<string, double>
            {
                ["taskAdherence"] = score,
                ["groundedness"] = score,
                ["toolAccuracy"] = score,
                ["latencyMilliseconds"] = 100,
                ["failureRate"] = samples == 0 ? 0 : (double)failed / samples,
                ["sampleCount"] = samples,
            });

    private static void AssertBaseline(
        IEnumerable<ShadowEvaluationRecord> records,
        string version,
        double taskAdherence,
        double groundedness,
        double toolAccuracy,
        int expectedCount)
    {
        ShadowEvaluationRecord[] matching =
            records.Where(record => record.CandidatePromptVersion == version).ToArray();
        Assert.Equal(expectedCount, matching.Length);
        Assert.All(matching, record => Assert.Equal(taskAdherence, record.Scores["taskAdherence"]));
        Assert.All(matching, record => Assert.Equal(groundedness, record.Scores["groundedness"]));
        Assert.All(matching, record => Assert.Equal(toolAccuracy, record.Scores["toolAccuracy"]));
    }

    private static string FindRepositoryPath(string child)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, child);
            if (Directory.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(child);
    }

    private sealed class FlakyContinuousProvider : IContinuousEvaluationProvider
    {
        private readonly FakeContinuousEvaluationProvider inner = new();
        private bool failed;

        public int CallCount { get; private set; }

        public Task<ContinuousEvaluationResult> EvaluateAsync(
            ContinuousEvaluationFixture fixture,
            string promptVersion,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (!failed && CallCount == 2)
            {
                failed = true;
                throw new InvalidOperationException("Transient evaluator failure.");
            }

            return inner.EvaluateAsync(fixture, promptVersion, cancellationToken);
        }
    }
}
