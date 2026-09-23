using GenAIOps.Application.Persistence;
using GenAIOps.Domain.Registry;

namespace GenAIOps.Application.Registry;

public sealed record RegistrySnapshot(AgentRegistryState State, string ETag);

public interface IAgentRegistryService
{
    Task<RegistrySnapshot?> GetAsync(string registryId, CancellationToken cancellationToken = default);

    Task<RegistrySnapshot> RegisterCandidateAsync(
        string registryId,
        string agentId,
        string promptVersion,
        string? expectedETag = null,
        CancellationToken cancellationToken = default);

    Task<RegistrySnapshot> PromoteCandidateAsync(
        string registryId,
        string agentId,
        string expectedETag,
        CancellationToken cancellationToken = default);

    Task<RegistrySnapshot> AssignProductionAsync(
        string registryId,
        string agentId,
        string promptVersion,
        string expectedETag,
        CancellationToken cancellationToken = default);
}

public sealed class AgentRegistryService(IRepository<AgentRegistryState> repository) : IAgentRegistryService
{
    public async Task<RegistrySnapshot?> GetAsync(
        string registryId,
        CancellationToken cancellationToken = default)
    {
        StoredItem<AgentRegistryState>? item =
            await repository.GetAsync(registryId, registryId, cancellationToken);

        return item is null ? null : new RegistrySnapshot(item.Value, item.ETag);
    }

    public async Task<RegistrySnapshot> RegisterCandidateAsync(
        string registryId,
        string agentId,
        string promptVersion,
        string? expectedETag = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);

        StoredItem<AgentRegistryState>? current =
            await repository.GetAsync(registryId, registryId, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AgentAssignment candidate = new(agentId, promptVersion, AssignmentSlot.Candidate, now);

        if (current is null)
        {
            if (expectedETag is not null)
            {
                throw new RecordConflictException(
                    nameof(AgentRegistryState),
                    registryId,
                    "the supplied ETag refers to a registry that does not exist.");
            }

            AgentRegistryState created = new(
                registryId,
                registryId,
                Production: null,
                Candidate: candidate,
                History: [],
                UpdatedAt: now);
            StoredItem<AgentRegistryState> stored =
                await repository.CreateAsync(created, cancellationToken);
            return new RegistrySnapshot(stored.Value, stored.ETag);
        }

        if (expectedETag is null)
        {
            throw new RecordConflictException(
                nameof(AgentRegistryState),
                registryId,
                "an ETag is required to replace the current candidate.");
        }

        EnsureExpectedETag(registryId, expectedETag, current.ETag);
        List<AgentAssignment> history = [.. current.Value.History];
        if (current.Value.Candidate is { } previousCandidate)
        {
            history.Add(previousCandidate with { UnassignedAt = now });
        }

        AgentRegistryState updated = current.Value with
        {
            Candidate = candidate,
            History = history,
            UpdatedAt = now,
        };
        StoredItem<AgentRegistryState> replaced =
            await repository.ReplaceAsync(updated, current.ETag, cancellationToken);
        return new RegistrySnapshot(replaced.Value, replaced.ETag);
    }

    public async Task<RegistrySnapshot> PromoteCandidateAsync(
        string registryId,
        string agentId,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);
        StoredItem<AgentRegistryState> current =
            await repository.GetAsync(registryId, registryId, cancellationToken)
            ?? throw new RecordNotFoundException(nameof(AgentRegistryState), registryId, registryId);

        EnsureExpectedETag(registryId, expectedETag, current.ETag);
        AgentAssignment candidate = current.Value.Candidate
            ?? throw new RecordConflictException(
                nameof(AgentRegistryState),
                registryId,
                "there is no candidate to promote.");
        if (!string.Equals(candidate.AgentId, agentId, StringComparison.Ordinal))
        {
            throw new RecordConflictException(
                nameof(AgentRegistryState),
                registryId,
                $"candidate '{agentId}' is not the current candidate.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<AgentAssignment> history = [.. current.Value.History];
        if (current.Value.Production is { } previousProduction)
        {
            history.Add(previousProduction with { UnassignedAt = now });
        }

        history.Add(candidate with { UnassignedAt = now });
        AgentRegistryState updated = current.Value with
        {
            Production = candidate with
            {
                Slot = AssignmentSlot.Production,
                AssignedAt = now,
                UnassignedAt = null,
            },
            Candidate = null,
            History = history,
            UpdatedAt = now,
        };
        StoredItem<AgentRegistryState> replaced =
            await repository.ReplaceAsync(updated, current.ETag, cancellationToken);
        return new RegistrySnapshot(replaced.Value, replaced.ETag);
    }

    public async Task<RegistrySnapshot> AssignProductionAsync(
        string registryId,
        string agentId,
        string promptVersion,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);
        StoredItem<AgentRegistryState> current =
            await repository.GetAsync(registryId, registryId, cancellationToken)
            ?? throw new RecordNotFoundException(nameof(AgentRegistryState), registryId, registryId);
        EnsureExpectedETag(registryId, expectedETag, current.ETag);

        if (current.Value.Production is { } production
            && string.Equals(production.AgentId, agentId, StringComparison.Ordinal)
            && string.Equals(production.PromptVersion, promptVersion, StringComparison.Ordinal))
        {
            return new RegistrySnapshot(current.Value, current.ETag);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<AgentAssignment> history = [.. current.Value.History];
        if (current.Value.Production is { } previousProduction)
        {
            history.Add(previousProduction with { UnassignedAt = now });
        }

        AgentAssignment? candidate = current.Value.Candidate;
        if (candidate is not null
            && string.Equals(candidate.AgentId, agentId, StringComparison.Ordinal)
            && string.Equals(candidate.PromptVersion, promptVersion, StringComparison.Ordinal))
        {
            history.Add(candidate with { UnassignedAt = now });
            candidate = null;
        }

        AgentRegistryState updated = current.Value with
        {
            Production = new AgentAssignment(
                agentId,
                promptVersion,
                AssignmentSlot.Production,
                now),
            Candidate = candidate,
            History = history,
            UpdatedAt = now,
        };
        StoredItem<AgentRegistryState> replaced =
            await repository.ReplaceAsync(updated, current.ETag, cancellationToken);
        return new RegistrySnapshot(replaced.Value, replaced.ETag);
    }

    private static void EnsureExpectedETag(string registryId, string? expected, string actual)
    {
        if (expected is not null && !string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new RecordConflictException(
                nameof(AgentRegistryState),
                registryId,
                "the supplied ETag is stale.");
        }
    }
}
