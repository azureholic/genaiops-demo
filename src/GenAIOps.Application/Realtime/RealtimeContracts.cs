namespace GenAIOps.Application.Realtime;

public sealed record MetricsUpdated(
    string PromptVersion,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int SampleCount,
    int SuccessfulCount,
    int FailureCount,
    IReadOnlyDictionary<string, double> Metrics,
    DateTimeOffset GeneratedAt);

public sealed record EvaluationUpdated(
    string ProductionPromptVersion,
    string CandidatePromptVersion,
    string Lifecycle,
    IReadOnlyDictionary<string, double> Scores,
    long? LatencyMilliseconds,
    DateTimeOffset CompletedAt);

public sealed record ExperimentAllocationUpdated(string PromptVersion, int Percentage);

public sealed record ExperimentUpdated(
    string Lifecycle,
    IReadOnlyList<ExperimentAllocationUpdated> Allocations,
    DateTimeOffset UpdatedAt);

public sealed record ReleaseUpdated(
    string Operation,
    string PromptVersion,
    string Lifecycle,
    DateTimeOffset OccurredAt);

public interface IRealtimeClient
{
    Task MetricsUpdated(MetricsUpdated update);

    Task EvaluationUpdated(EvaluationUpdated update);

    Task ExperimentUpdated(ExperimentUpdated update);

    Task ReleaseUpdated(ReleaseUpdated update);
}

public interface IRealtimePublisher
{
    Task PublishMetricsAsync(
        MetricsUpdated update,
        CancellationToken cancellationToken = default);

    Task PublishEvaluationAsync(
        EvaluationUpdated update,
        CancellationToken cancellationToken = default);

    Task PublishExperimentAsync(
        ExperimentUpdated update,
        CancellationToken cancellationToken = default);

    Task PublishReleaseAsync(
        ReleaseUpdated update,
        CancellationToken cancellationToken = default);
}

public sealed class NoOpRealtimePublisher : IRealtimePublisher
{
    public static NoOpRealtimePublisher Instance { get; } = new();

    public Task PublishMetricsAsync(
        MetricsUpdated update,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

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
