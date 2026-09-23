using GenAIOps.Domain.Records;

namespace GenAIOps.Application.Metrics;

public sealed record AggregationWindow(
    string RegistryId,
    DateTimeOffset Start,
    DateTimeOffset End)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RegistryId);
        if (Start.Offset != TimeSpan.Zero || End.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Aggregation window boundaries must be UTC.");
        }

        if (End <= Start)
        {
            throw new ArgumentException("Aggregation window end must be after its start.");
        }
    }
}

public interface IMetricsAggregator
{
    Task<IReadOnlyList<MetricSnapshotRecord>> AggregateAsync(
        AggregationWindow window,
        CancellationToken cancellationToken = default);
}

public sealed record MetricsQuery(
    string RegistryId,
    string? PromptVersion,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int PageSize,
    string? ContinuationToken);

public sealed record MetricsPage(
    IReadOnlyList<MetricSnapshotRecord> Items,
    string? ContinuationToken);

public interface IMetricsQueryService
{
    Task<MetricsPage> QueryAsync(
        MetricsQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record ContinuousEvaluationFixture(
    string Id,
    string UserMessage,
    IReadOnlyList<string> PromptVersions,
    IReadOnlyList<string> ExpectedResponseTerms,
    string RequiredTool);

public sealed record ContinuousEvaluationResult(
    string Output,
    double TaskAdherence,
    double Groundedness,
    double ToolAccuracy,
    long LatencyMilliseconds);

public interface IContinuousEvaluationProvider
{
    Task<ContinuousEvaluationResult> EvaluateAsync(
        ContinuousEvaluationFixture fixture,
        string promptVersion,
        CancellationToken cancellationToken = default);
}

public interface IContinuousEvaluationRunner
{
    Task<int> RunAsync(
        string registryId,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken = default);
}
