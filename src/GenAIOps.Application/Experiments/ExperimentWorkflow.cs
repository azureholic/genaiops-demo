using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using GenAIOps.Application.Chat;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;

namespace GenAIOps.Application.Experiments;

public enum ExperimentCommandAction
{
    Start,
    Update,
    End,
}

public sealed record ExperimentCommand(
    string RegistryId,
    ExperimentCommandAction Action,
    string Actor,
    string? Name,
    IReadOnlyList<ExperimentAllocation>? Allocations,
    string? ExpectedETag);

public sealed record ExperimentResult(ExperimentRecord Experiment, string ETag);

public interface IExperimentService
{
    Task<ExperimentResult> ExecuteAsync(
        ExperimentCommand command,
        CancellationToken cancellationToken = default);
}

public interface IExperimentRouter
{
    Task<ExperimentAssignment> AssignAsync(
        string registryId,
        RegistrySnapshot registry,
        string? assignmentKey,
        CancellationToken cancellationToken = default);
}

public abstract class ExperimentException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ExperimentValidationException(string message)
    : ExperimentException("invalid_experiment", message);

public sealed class ExperimentNotFoundException(string message)
    : ExperimentException("experiment_not_found", message);

public sealed class ExperimentEligibilityException(string message)
    : ExperimentException("ineligible_version", message);

public sealed class ExperimentConcurrencyException(string message)
    : ExperimentException("stale_experiment", message);

public sealed class ExperimentLifecycleException(string message)
    : ExperimentException("invalid_experiment_transition", message);

public sealed class ExperimentService(
    IRepository<ExperimentRecord> experiments,
    IAgentRegistryService registry) : IExperimentService
{
    public const string RecordId = "active-abtest";

    public async Task<ExperimentResult> ExecuteAsync(
        ExperimentCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);
        StoredItem<ExperimentRecord>? current = await experiments.GetAsync(
            RecordId,
            command.RegistryId,
            cancellationToken);
        return command.Action switch
        {
            ExperimentCommandAction.Start =>
                await StartAsync(command, current, cancellationToken),
            ExperimentCommandAction.Update =>
                await UpdateAsync(command, current, cancellationToken),
            ExperimentCommandAction.End =>
                await EndAsync(command, current, cancellationToken),
            _ => throw new ExperimentValidationException("Unsupported experiment action."),
        };
    }

    private async Task<ExperimentResult> StartAsync(
        ExperimentCommand command,
        StoredItem<ExperimentRecord>? current,
        CancellationToken cancellationToken)
    {
        if (current?.Value.Lifecycle == ExperimentLifecycle.Running)
        {
            throw new ExperimentLifecycleException(
                "A running experiment already exists for this registry.");
        }

        IReadOnlyList<ExperimentAllocation> allocations =
            await ValidateAllocationsAsync(command, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ExperimentRecord record = new(
            RecordId,
            command.RegistryId,
            $"experiment-{Guid.NewGuid():N}",
            command.RegistryId,
            command.Name!,
            allocations,
            ExperimentLifecycle.Running,
            command.Actor,
            now,
            now,
            now,
            EndedAt: null);
        try
        {
            StoredItem<ExperimentRecord> created = current is null
                ? await experiments.CreateAsync(record, cancellationToken)
                : await experiments.ReplaceAsync(record, current.ETag, cancellationToken);
            return new ExperimentResult(created.Value, created.ETag);
        }
        catch (RecordConflictException exception)
        {
            throw new ExperimentConcurrencyException(exception.Message);
        }
    }

    private async Task<ExperimentResult> UpdateAsync(
        ExperimentCommand command,
        StoredItem<ExperimentRecord>? current,
        CancellationToken cancellationToken)
    {
        ExperimentRecord existing = RequireRunning(current);
        EnsureETag(command, current!);
        IReadOnlyList<ExperimentAllocation> allocations =
            await ValidateAllocationsAsync(command, cancellationToken);
        ExperimentRecord updated = existing with
        {
            Name = command.Name ?? existing.Name,
            Allocations = allocations,
            Actor = command.Actor,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return await ReplaceAsync(updated, current!.ETag, cancellationToken);
    }

    private async Task<ExperimentResult> EndAsync(
        ExperimentCommand command,
        StoredItem<ExperimentRecord>? current,
        CancellationToken cancellationToken)
    {
        ExperimentRecord existing = RequireRunning(current);
        EnsureETag(command, current!);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return await ReplaceAsync(
            existing with
            {
                Lifecycle = ExperimentLifecycle.Completed,
                Actor = command.Actor,
                UpdatedAt = now,
                EndedAt = now,
            },
            current!.ETag,
            cancellationToken);
    }

    private async Task<ExperimentResult> ReplaceAsync(
        ExperimentRecord record,
        string expectedETag,
        CancellationToken cancellationToken)
    {
        try
        {
            StoredItem<ExperimentRecord> replaced =
                await experiments.ReplaceAsync(record, expectedETag, cancellationToken);
            return new ExperimentResult(replaced.Value, replaced.ETag);
        }
        catch (RecordConflictException exception)
        {
            throw new ExperimentConcurrencyException(exception.Message);
        }
    }

    private async Task<IReadOnlyList<ExperimentAllocation>> ValidateAllocationsAsync(
        ExperimentCommand command,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExperimentAllocation> allocations = command.Allocations
            ?? throw new ExperimentValidationException(
                "Allocations are required for start and update.");
        if (allocations.Count < 2
            || allocations.Any(item =>
                string.IsNullOrWhiteSpace(item.PromptVersion)
                || item.Percentage is <= 0 or > 100)
            || allocations.Select(item => item.PromptVersion).Distinct(StringComparer.Ordinal).Count()
                != allocations.Count
            || allocations.Sum(item => (long)item.Percentage) != 100)
        {
            throw new ExperimentValidationException(
                "Allocations require at least two distinct versions with positive percentages totaling 100.");
        }

        RegistrySnapshot snapshot = await registry.GetAsync(command.RegistryId, cancellationToken)
            ?? throw new ExperimentEligibilityException(
                $"Registry '{command.RegistryId}' was not found.");
        Dictionary<string, AgentAssignment> eligible = GetEligibleAssignments(snapshot.State)
            .GroupBy(item => item.PromptVersion, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.AssignedAt).First(),
                StringComparer.Ordinal);
        string[] ineligible = allocations
            .Select(item => item.PromptVersion)
            .Where(version => !eligible.ContainsKey(version))
            .ToArray();
        if (ineligible.Length > 0)
        {
            throw new ExperimentEligibilityException(
                $"Versions are not approved for production routing: {string.Join(", ", ineligible)}.");
        }

        return allocations
            .Select(item => item with { AgentId = eligible[item.PromptVersion].AgentId })
            .ToArray();
    }

    private static ExperimentRecord RequireRunning(StoredItem<ExperimentRecord>? current)
    {
        if (current is null)
        {
            throw new ExperimentNotFoundException("No experiment exists for this registry.");
        }

        if (current.Value.Lifecycle != ExperimentLifecycle.Running)
        {
            throw new ExperimentLifecycleException(
                $"Experiment cannot change from lifecycle '{current.Value.Lifecycle}'.");
        }

        return current.Value;
    }

    private static void EnsureETag(
        ExperimentCommand command,
        StoredItem<ExperimentRecord> current)
    {
        if (string.IsNullOrWhiteSpace(command.ExpectedETag)
            || !string.Equals(command.ExpectedETag, current.ETag, StringComparison.Ordinal))
        {
            throw new ExperimentConcurrencyException(
                "A current If-Match experiment ETag is required.");
        }
    }

    private static void ValidateCommand(ExperimentCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.RegistryId)
            || string.IsNullOrWhiteSpace(command.Actor)
            || command.Actor.Length > 200)
        {
            throw new ExperimentValidationException(
                "Registry ID and an actor of at most 200 characters are required.");
        }

        if (command.Action == ExperimentCommandAction.Start
            && string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ExperimentValidationException("Experiment name is required when starting.");
        }
    }

    internal static IEnumerable<AgentAssignment> GetEligibleAssignments(AgentRegistryState state)
    {
        if (state.Production is not null)
        {
            yield return state.Production;
        }

        foreach (AgentAssignment assignment in state.History.Where(
                     item => item.Slot == AssignmentSlot.Production))
        {
            yield return assignment;
        }
    }
}

public sealed class ExperimentRouter(IRepository<ExperimentRecord> experiments) : IExperimentRouter
{
    public const int MaximumAssignmentKeyLength = 256;

    public async Task<ExperimentAssignment> AssignAsync(
        string registryId,
        RegistrySnapshot registry,
        string? assignmentKey,
        CancellationToken cancellationToken = default)
    {
        StoredItem<ExperimentRecord>? stored = await experiments.GetAsync(
            ExperimentService.RecordId,
            registryId,
            cancellationToken);
        if (stored?.Value.Lifecycle != ExperimentLifecycle.Running)
        {
            AgentAssignment production = registry.State.Production
                ?? throw new ProductionAgentNotFoundException(registryId);
            return new ExperimentAssignment(
                ExperimentId: null,
                production.AgentId,
                production.PromptVersion,
                DateTimeOffset.UtcNow);
        }

        if (string.IsNullOrWhiteSpace(assignmentKey)
            || assignmentKey.Length > MaximumAssignmentKeyLength)
        {
            throw new ChatValidationException(
                $"X-Assignment-Key is required during an experiment and must not exceed {MaximumAssignmentKeyLength} characters.");
        }

        int bucket = GetBucket(stored.Value.ExperimentId, assignmentKey);
        int upperBound = 0;
        ExperimentAllocation selected = stored.Value.Allocations.First(
            allocation =>
            {
                upperBound += allocation.Percentage;
                return bucket < upperBound;
            });
        return new ExperimentAssignment(
            stored.Value.ExperimentId,
            selected.AgentId
                ?? throw new ExperimentLifecycleException(
                    "The experiment allocation is missing its approved agent assignment."),
            selected.PromptVersion,
            DateTimeOffset.UtcNow);
    }

    public static int GetBucket(string experimentId, string assignmentKey)
    {
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{experimentId}\n{assignmentKey}"));
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(digest) % 100);
    }
}
