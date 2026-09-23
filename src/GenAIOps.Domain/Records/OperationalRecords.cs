using GenAIOps.Domain.Persistence;
using GenAIOps.Domain.Registry;

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
    Poisoned,
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
    Rejected,
    RolledBack,
    Superseded,
}

public enum ReleaseOperation
{
    Promotion,
    Rollback,
}

public sealed record ReleaseCommandRecord(
    string Id,
    string PartitionKey,
    string RegistryId,
    string IdempotencyKey,
    ReleaseOperation Operation,
    string? PromptVersion,
    string Actor,
    string ExpectedETag,
    AgentAssignment Target,
    AgentAssignment? PreviousProduction,
    ReleaseGateEvidence? GateEvidence,
    DateTimeOffset CreatedAt) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "releaseCommand";
}

public enum ShadowWorkLifecycle
{
    Pending,
    Processing,
    Completed,
    DeadLettered,
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
    DateTimeOffset GeneratedAt,
    int SampleCount,
    int SuccessfulCount,
    int FailureCount,
    IReadOnlyList<string> SourceEvaluationIds,
    IReadOnlyDictionary<string, double> Metrics) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "metricSnapshot";
}

public sealed record ExperimentRecord(
    string Id,
    string PartitionKey,
    string ExperimentId,
    string RegistryId,
    string Name,
    IReadOnlyList<ExperimentAllocation> Allocations,
    ExperimentLifecycle Lifecycle,
    string Actor,
    DateTimeOffset CreatedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? EndedAt) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "experiment";
}

public sealed record ExperimentAllocation(
    string PromptVersion,
    int Percentage,
    string? AgentId = null);

public sealed record ExperimentAssignment(
    string? ExperimentId,
    string AgentId,
    string PromptVersion,
    DateTimeOffset AssignedAt);

public sealed record ReleaseRecord(
    string Id,
    string PartitionKey,
    string RegistryId,
    string IdempotencyKey,
    ReleaseOperation Operation,
    string PromptVersion,
    string AgentId,
    ReleaseLifecycle Lifecycle,
    string Actor,
    DateTimeOffset CreatedAt,
    AgentAssignment? PreviousProduction,
    AgentAssignment? NewProduction,
    AgentAssignment? RollbackTarget,
    ReleaseGateEvidence? GateEvidence,
    string ExpectedETag,
    AgentRegistryState? ResultRegistry,
    string? ResultETag) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "release";
}

public sealed record ReleaseGateEvidence(
    string MetricSnapshotId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int SampleCount,
    IReadOnlyDictionary<string, double> Observed,
    IReadOnlyDictionary<string, double> Thresholds,
    bool Passed,
    IReadOnlyList<string> Reasons);

public sealed record ChatRequestMetadataRecord(
    string Id,
    string PartitionKey,
    string CorrelationId,
    string RegistryId,
    string AgentId,
    string PromptVersion,
    DateTimeOffset RequestedAt,
    int InputCharacterCount,
    string Outcome,
    string? ProviderResponseId,
    string? ExperimentId = null,
    string? AssignedPromptVersion = null) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "chatRequestMetadata";
}

public sealed record ShadowEvaluationRecord(
    string Id,
    string PartitionKey,
    string CorrelationId,
    string RegistryId,
    string ProductionAgentId,
    string ProductionPromptVersion,
    string CandidateAgentId,
    string CandidatePromptVersion,
    string Input,
    string ProductionOutput,
    string? CandidateOutput,
    EvaluationLifecycle Lifecycle,
    IReadOnlyDictionary<string, double> Scores,
    long? CandidateLatencyMilliseconds,
    int AttemptCount,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ExperimentId = null,
    string? AssignedPromptVersion = null,
    string? TraceParent = null,
    string? TraceState = null) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "shadowEvaluation";
}

public sealed record ShadowWorkRecord(
    string Id,
    string PartitionKey,
    string CorrelationId,
    string RegistryId,
    string ProductionAgentId,
    string ProductionPromptVersion,
    string CandidateAgentId,
    string CandidatePromptVersion,
    string Input,
    string ProductionOutput,
    DateTimeOffset PublishedAt,
    ShadowWorkLifecycle Lifecycle,
    int AttemptCount,
    string? DeadLetterReason,
    string? ExperimentId = null,
    string? AssignedPromptVersion = null,
    string? TraceParent = null,
    string? TraceState = null) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "shadowWork";
}
