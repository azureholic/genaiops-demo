using GenAIOps.Domain.Persistence;

namespace GenAIOps.Domain.Registry;

public enum AgentLifecycle
{
    Provisioning,
    Active,
    Retired,
    Failed,
}

public enum AssignmentSlot
{
    Candidate,
    Production,
}

public sealed record AgentRecord(
    string Id,
    string PartitionKey,
    string Name,
    string FoundryAgentId,
    string PromptVersion,
    AgentLifecycle Lifecycle,
    DateTimeOffset CreatedAt) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "agent";
}

public sealed record AgentAssignment(
    string AgentId,
    string PromptVersion,
    AssignmentSlot Slot,
    DateTimeOffset AssignedAt,
    DateTimeOffset? UnassignedAt = null);

public sealed record AgentRegistryState(
    string Id,
    string PartitionKey,
    AgentAssignment? Production,
    AgentAssignment? Candidate,
    IReadOnlyList<AgentAssignment> History,
    DateTimeOffset UpdatedAt) : IPersistedRecord
{
    public int SchemaVersion => 1;

    public string Type => "agentRegistry";
}
