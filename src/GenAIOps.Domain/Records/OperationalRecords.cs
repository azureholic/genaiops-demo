using GenAIOps.Domain.Persistence;

namespace GenAIOps.Domain.Records;

public enum PromptVersionLifecycle
{
    Draft,
    Candidate,
    Production,
    Retired,
}

public enum DeploymentLifecycle
{
    Pending,
    Deploying,
    Active,
    Failed,
    Retired,
}

public enum EvaluationLifecycle
{
    Pending,
    Running,
    Completed,
    Failed,
}

public enum ExperimentLifecycle
{
    Draft,
    Running,
    Paused,
    Completed,
    Cancelled,
}

public enum ReleaseLifecycle
{
    Created,
    Promoted,
    RolledBack,
    Superseded,
}

public sealed record PromptVersionRecord(
    string Id,
    string PartitionKey,
    string Version,
    string DisplayName,
    string ContentHash,
    PromptVersionLifecycle Lifecycle,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, double> ExpectedMetrics) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "promptVersion";
}

public sealed record DeploymentRecord(
    string Id,
    string PartitionKey,
    string AgentId,
    string PromptVersion,
    DeploymentLifecycle Lifecycle,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "deployment";
}

public sealed record EvaluationRecord(
    string Id,
    string PartitionKey,
    string PromptVersion,
    EvaluationLifecycle Lifecycle,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyDictionary<string, double> Scores) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "evaluation";
}

public sealed record MetricSnapshotRecord(
    string Id,
    string PartitionKey,
    string PromptVersion,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    IReadOnlyDictionary<string, double> Metrics) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "metricSnapshot";
}

public sealed record ExperimentRecord(
    string Id,
    string PartitionKey,
    string Name,
    IReadOnlyList<string> PromptVersions,
    ExperimentLifecycle Lifecycle,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "experiment";
}

public sealed record ReleaseRecord(
    string Id,
    string PartitionKey,
    string PromptVersion,
    string AgentId,
    ReleaseLifecycle Lifecycle,
    DateTimeOffset CreatedAt,
    string? ReplacesReleaseId) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "release";
}
