using System.Security.Cryptography;
using System.Text;
using GenAIOps.Application.Persistence;
using GenAIOps.Domain.Records;

namespace GenAIOps.Application.Metrics;

public sealed class MetricsAggregator(
    IRepository<ShadowEvaluationRecord> evaluations,
    IRepository<MetricSnapshotRecord> snapshots) : IMetricsAggregator
{
    public async Task<IReadOnlyList<MetricSnapshotRecord>> AggregateAsync(
        AggregationWindow window,
        CancellationToken cancellationToken = default)
    {
        window.Validate();
        IReadOnlyList<ShadowEvaluationRecord> records =
            await ReadEvaluationsAsync(window.RegistryId, cancellationToken);
        ShadowEvaluationRecord[] inWindow = records
            .Where(record =>
                record.CreatedAt >= window.Start
                && record.CreatedAt < window.End
                && record.Lifecycle is EvaluationLifecycle.Completed
                    or EvaluationLifecycle.Failed
                    or EvaluationLifecycle.Poisoned)
            .DistinctBy(record => record.Id, StringComparer.Ordinal)
            .ToArray();
        List<MetricSnapshotRecord> results = [];

        foreach (IGrouping<string, ShadowEvaluationRecord> group in inWindow.GroupBy(
            record => record.CandidatePromptVersion,
            StringComparer.Ordinal))
        {
            string[] sourceIds = group
                .Select(record => record.Id)
                .Order(StringComparer.Ordinal)
                .ToArray();
            int sampleCount = sourceIds.Length;
            ShadowEvaluationRecord[] successful = group
                .Where(record => record.Lifecycle == EvaluationLifecycle.Completed)
                .ToArray();
            int failureCount = sampleCount - successful.Length;
            Dictionary<string, double> metrics = new(StringComparer.Ordinal)
            {
                ["taskAdherence"] = AverageScore(successful, "taskAdherence"),
                ["groundedness"] = AverageScore(successful, "groundedness"),
                ["toolAccuracy"] = AverageScore(successful, "toolAccuracy"),
                ["latencyMilliseconds"] = group
                    .Where(record => record.CandidateLatencyMilliseconds.HasValue)
                    .Select(record => (double)record.CandidateLatencyMilliseconds!.Value)
                    .DefaultIfEmpty(0)
                    .Average(),
                ["failureRate"] = sampleCount == 0 ? 0 : (double)failureCount / sampleCount,
                ["sampleCount"] = sampleCount,
            };
            MetricSnapshotRecord snapshot = new(
                CreateSnapshotId(window, group.Key, sourceIds),
                window.RegistryId,
                group.Key,
                window.Start,
                window.End,
                DateTimeOffset.UtcNow,
                sampleCount,
                successful.Length,
                failureCount,
                sourceIds,
                metrics);
            results.Add(await CreateIdempotentlyAsync(snapshot, cancellationToken));
        }

        return results;
    }

    public static IReadOnlyDictionary<string, double> CombineWeighted(
        IEnumerable<MetricSnapshotRecord> snapshots)
    {
        MetricSnapshotRecord[] records = snapshots
            .GroupBy(
                snapshot => new
                {
                    snapshot.PartitionKey,
                    snapshot.PromptVersion,
                    snapshot.WindowStart,
                    snapshot.WindowEnd,
                })
            .Select(
                revisions => revisions
                    .OrderByDescending(snapshot => snapshot.SampleCount)
                    .ThenByDescending(snapshot => snapshot.GeneratedAt)
                    .First())
            .ToArray();
        int totalSamples = records.Sum(record => record.SampleCount);
        int totalSuccesses = records.Sum(record => record.SuccessfulCount);
        int totalFailures = records.Sum(record => record.FailureCount);
        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["taskAdherence"] = Weighted(records, "taskAdherence", record => record.SuccessfulCount),
            ["groundedness"] = Weighted(records, "groundedness", record => record.SuccessfulCount),
            ["toolAccuracy"] = Weighted(records, "toolAccuracy", record => record.SuccessfulCount),
            ["latencyMilliseconds"] =
                Weighted(records, "latencyMilliseconds", record => record.SampleCount),
            ["failureRate"] = totalSamples == 0 ? 0 : (double)totalFailures / totalSamples,
            ["sampleCount"] = totalSamples,
            ["successfulCount"] = totalSuccesses,
        };
    }

    private async Task<IReadOnlyList<ShadowEvaluationRecord>> ReadEvaluationsAsync(
        string registryId,
        CancellationToken cancellationToken)
    {
        List<ShadowEvaluationRecord> records = [];
        string? continuationToken = null;
        do
        {
            RepositoryPage<ShadowEvaluationRecord> page = await evaluations.QueryAsync(
                new RecordQuery(
                    registryId,
                    "shadowEvaluation",
                    RecordQuery.MaximumPageSize,
                    continuationToken),
                cancellationToken);
            records.AddRange(page.Items.Select(item => item.Value));
            continuationToken = page.ContinuationToken;
        }
        while (continuationToken is not null);

        return records;
    }

    private async Task<MetricSnapshotRecord> CreateIdempotentlyAsync(
        MetricSnapshotRecord snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await snapshots.CreateAsync(snapshot, cancellationToken)).Value;
        }
        catch (RecordConflictException)
        {
            StoredItem<MetricSnapshotRecord>? existing = await snapshots.GetAsync(
                snapshot.Id,
                snapshot.PartitionKey,
                cancellationToken);
            return existing?.Value
                ?? throw new InvalidOperationException(
                    $"Metric snapshot '{snapshot.Id}' conflicted but could not be read.");
        }
    }

    private static double AverageScore(
        IEnumerable<ShadowEvaluationRecord> records,
        string metricName) =>
        records
            .Where(record => record.Scores.ContainsKey(metricName))
            .Select(record => record.Scores[metricName])
            .DefaultIfEmpty(0)
            .Average();

    private static double Weighted(
        IEnumerable<MetricSnapshotRecord> snapshots,
        string metricName,
        Func<MetricSnapshotRecord, int> weightSelector)
    {
        MetricSnapshotRecord[] applicable = snapshots
            .Where(record => record.Metrics.ContainsKey(metricName) && weightSelector(record) > 0)
            .ToArray();
        int totalWeight = applicable.Sum(weightSelector);
        return totalWeight == 0
            ? 0
            : applicable.Sum(
                record => record.Metrics[metricName] * weightSelector(record)) / totalWeight;
    }

    private static string CreateSnapshotId(
        AggregationWindow window,
        string promptVersion,
        IEnumerable<string> sourceIds)
    {
        string source = string.Join(
            '\n',
            window.RegistryId,
            promptVersion,
            window.Start.ToString("O"),
            window.End.ToString("O"),
            string.Join('\n', sourceIds));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return $"metrics-{promptVersion}-{hash[..24].ToLowerInvariant()}";
    }
}
