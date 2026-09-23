using System.Security.Cryptography;
using System.Text;
using GenAIOps.Application.Metrics;
using GenAIOps.Application.Observability;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Realtime;
using GenAIOps.Application.Registry;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;

namespace GenAIOps.Application.Releases;

public sealed record QualityGateOptions(
    int MinimumSamples,
    double MinimumTaskAdherence,
    double MinimumGroundedness,
    double MinimumToolAccuracy,
    double MaximumFailureRate,
    double? MaximumLatencyMilliseconds)
{
    public void Validate()
    {
        if (MinimumSamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSamples));
        }

        ValidateRange(MinimumTaskAdherence, nameof(MinimumTaskAdherence));
        ValidateRange(MinimumGroundedness, nameof(MinimumGroundedness));
        ValidateRange(MinimumToolAccuracy, nameof(MinimumToolAccuracy));
        ValidateRange(MaximumFailureRate, nameof(MaximumFailureRate));
        if (MaximumLatencyMilliseconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumLatencyMilliseconds));
        }
    }

    public IReadOnlyDictionary<string, double> ToDictionary()
    {
        Dictionary<string, double> thresholds = new(StringComparer.Ordinal)
        {
            ["minimumSamples"] = MinimumSamples,
            ["taskAdherence"] = MinimumTaskAdherence,
            ["groundedness"] = MinimumGroundedness,
            ["toolAccuracy"] = MinimumToolAccuracy,
            ["maximumFailureRate"] = MaximumFailureRate,
        };
        if (MaximumLatencyMilliseconds.HasValue)
        {
            thresholds["maximumLatencyMilliseconds"] = MaximumLatencyMilliseconds.Value;
        }

        return thresholds;
    }

    private static void ValidateRange(double value, string name)
    {
        if (value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name, "Quality threshold must be between 0 and 1.");
        }
    }
}

public sealed record ReleaseCommand(
    string RegistryId,
    string Actor,
    string IdempotencyKey,
    string ExpectedETag);

public sealed record ReleaseWorkflowResult(
    RegistrySnapshot Registry,
    ReleaseRecord Release,
    bool Replayed);

public interface IReleaseWorkflowService
{
    Task<ReleaseWorkflowResult> PromoteAsync(
        string version,
        ReleaseCommand command,
        CancellationToken cancellationToken = default);

    Task<ReleaseWorkflowResult> RollbackAsync(
        ReleaseCommand command,
        CancellationToken cancellationToken = default);
}

public abstract class ReleaseWorkflowException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ReleaseValidationException(string message)
    : ReleaseWorkflowException("invalid_release_command", message);

public sealed class ReleaseStateNotFoundException(string message)
    : ReleaseWorkflowException("release_state_not_found", message);

public sealed class ReleaseConcurrencyException(string message)
    : ReleaseWorkflowException("stale_registry", message);

public sealed class ReleaseIdempotencyConflictException(string message)
    : ReleaseWorkflowException("idempotency_conflict", message);

public sealed class QualityGateRejectedException(
    string message,
    ReleaseGateEvidence evidence)
    : ReleaseWorkflowException("quality_gate_rejected", message)
{
    public ReleaseGateEvidence Evidence { get; } = evidence;
}

public sealed class ReleaseWorkflowService(
    IAgentRegistryService registry,
    IMetricsQueryService metrics,
    IRepository<ReleaseRecord> releases,
    IRepository<ReleaseCommandRecord> commands,
    QualityGateOptions gates,
    IRealtimePublisher? realtimePublisher = null) : IReleaseWorkflowService
{
    private readonly IRealtimePublisher realtime =
        realtimePublisher ?? NoOpRealtimePublisher.Instance;

    public async Task<ReleaseWorkflowResult> PromoteAsync(
        string version,
        ReleaseCommand command,
        CancellationToken cancellationToken = default)
    {
        using GenAIOpsTelemetry.TelemetryOperation operation =
            GenAIOpsTelemetry.StartOperation("release.promote", "promotion", version);
        try
        {
            ReleaseWorkflowResult result =
                await PromoteCoreAsync(version, command, cancellationToken);
            if (!result.Replayed)
            {
                await PublishAsync(result.Release, cancellationToken);
            }
            operation.Complete(
                "success",
                lifecycle: result.Release.Lifecycle.ToString().ToLowerInvariant());
            return result;
        }
        catch (QualityGateRejectedException)
        {
            operation.Complete("rejected", lifecycle: "rejected", gateResult: "failed");
            throw;
        }
        catch
        {
            operation.Complete("failure");
            throw;
        }
    }

    private async Task<ReleaseWorkflowResult> PromoteCoreAsync(
        string version,
        ReleaseCommand command,
        CancellationToken cancellationToken)
    {
        Validate(version, command);
        string releaseId = CreateReleaseId(command.RegistryId, command.IdempotencyKey);
        ReleaseRecord? replay = await GetReplayAsync(
            releaseId,
            command,
            ReleaseOperation.Promotion,
            version,
            cancellationToken);
        if (replay is not null)
        {
            if (replay.Lifecycle == ReleaseLifecycle.Rejected)
            {
                throw new QualityGateRejectedException(
                    "The candidate did not satisfy the configured quality gates.",
                    replay.GateEvidence!);
            }

            return new ReleaseWorkflowResult(
                GetRecordedResult(replay),
                replay,
                Replayed: true);
        }

        RegistrySnapshot before = await RequireRegistryAsync(
            command.RegistryId,
            cancellationToken);
        EnsureETag(command, before);
        AgentAssignment candidate = before.State.Candidate
            ?? throw new ReleaseStateNotFoundException(
                $"Registry '{command.RegistryId}' has no candidate assignment.");
        if (!string.Equals(candidate.PromptVersion, version, StringComparison.Ordinal))
        {
            throw new ReleaseStateNotFoundException(
                $"Candidate version '{version}' is not assigned to registry '{command.RegistryId}'.");
        }

        ReleaseGateEvidence evidence =
            await EvaluateGatesAsync(command.RegistryId, version, cancellationToken);
        if (!evidence.Passed)
        {
            ReleaseRecord rejected = new(
                releaseId,
                command.RegistryId,
                command.RegistryId,
                command.IdempotencyKey,
                ReleaseOperation.Promotion,
                candidate.PromptVersion,
                candidate.AgentId,
                ReleaseLifecycle.Rejected,
                command.Actor,
                DateTimeOffset.UtcNow,
                before.State.Production,
                null,
                before.State.Production,
                evidence,
                command.ExpectedETag,
                ResultRegistry: null,
                ResultETag: null);
            await CreateReleaseIdempotentlyAsync(rejected, cancellationToken);
            throw new QualityGateRejectedException(
                $"Candidate version '{version}' failed: {string.Join("; ", evidence.Reasons)}",
                evidence);
        }

        ReleaseCommandRecord receipt = await ReserveAsync(
            releaseId,
            command,
            ReleaseOperation.Promotion,
            candidate,
            before.State.Production,
            evidence,
            cancellationToken);
        before = await RequireRegistryAsync(command.RegistryId, cancellationToken);
        if (Matches(before.State.Production, receipt.Target))
        {
            return await RecoverAsync(receipt, before, replayed: true);
        }

        EnsureETag(command, before);
        RegistrySnapshot after;
        try
        {
            after = await registry.PromoteCandidateAsync(
                command.RegistryId,
                candidate.AgentId,
                command.ExpectedETag,
                cancellationToken);
        }
        catch (RecordConflictException exception)
        {
            RegistrySnapshot concurrent = await RequireRegistryAsync(
                command.RegistryId,
                CancellationToken.None);
            if (Matches(concurrent.State.Production, receipt.Target))
            {
                return await RecoverAsync(receipt, concurrent, replayed: true);
            }

            throw new ReleaseConcurrencyException(exception.Message);
        }

        ReleaseRecord release = new(
            releaseId,
            command.RegistryId,
            command.RegistryId,
            command.IdempotencyKey,
            ReleaseOperation.Promotion,
            candidate.PromptVersion,
            candidate.AgentId,
            ReleaseLifecycle.Promoted,
            command.Actor,
            DateTimeOffset.UtcNow,
            before.State.Production,
            after.State.Production,
            before.State.Production,
            evidence,
            command.ExpectedETag,
            after.State,
            after.ETag);
        ReleaseRecord stored = await CreateReleaseIdempotentlyAsync(release, CancellationToken.None);
        return new ReleaseWorkflowResult(after, stored, Replayed: false);
    }

    public async Task<ReleaseWorkflowResult> RollbackAsync(
        ReleaseCommand command,
        CancellationToken cancellationToken = default)
    {
        using GenAIOpsTelemetry.TelemetryOperation operation =
            GenAIOpsTelemetry.StartOperation("release.rollback", "rollback");
        try
        {
            ReleaseWorkflowResult result = await RollbackCoreAsync(command, cancellationToken);
            if (!result.Replayed)
            {
                await PublishAsync(result.Release, cancellationToken);
            }
            operation.SetPromptVersion(result.Release.PromptVersion);
            operation.Complete(
                "success",
                lifecycle: result.Release.Lifecycle.ToString().ToLowerInvariant());
            return result;
        }
        catch
        {
            operation.Complete("failure");
            throw;
        }
    }

    private Task PublishAsync(ReleaseRecord release, CancellationToken cancellationToken) =>
        realtime.PublishReleaseAsync(
            new ReleaseUpdated(
                release.Operation.ToString().ToLowerInvariant(),
                release.PromptVersion,
                release.Lifecycle.ToString().ToLowerInvariant(),
                release.CreatedAt),
            cancellationToken);

    private async Task<ReleaseWorkflowResult> RollbackCoreAsync(
        ReleaseCommand command,
        CancellationToken cancellationToken)
    {
        Validate(version: null, command);
        string releaseId = CreateReleaseId(command.RegistryId, command.IdempotencyKey);
        ReleaseRecord? replay = await GetReplayAsync(
            releaseId,
            command,
            ReleaseOperation.Rollback,
            version: null,
            cancellationToken);
        if (replay is not null)
        {
            return new ReleaseWorkflowResult(
                GetRecordedResult(replay),
                replay,
                Replayed: true);
        }

        RegistrySnapshot before = await RequireRegistryAsync(
            command.RegistryId,
            cancellationToken);
        EnsureETag(command, before);
        AgentAssignment current = before.State.Production
            ?? throw new ReleaseStateNotFoundException(
                $"Registry '{command.RegistryId}' has no production assignment.");
        AgentAssignment target = before.State.History
            .Where(assignment => assignment.Slot == AssignmentSlot.Production)
            .Where(assignment =>
                !string.Equals(assignment.AgentId, current.AgentId, StringComparison.Ordinal)
                || !string.Equals(
                    assignment.PromptVersion,
                    current.PromptVersion,
                    StringComparison.Ordinal))
            .OrderByDescending(assignment => assignment.UnassignedAt ?? assignment.AssignedAt)
            .FirstOrDefault()
            ?? throw new ReleaseStateNotFoundException(
                $"Registry '{command.RegistryId}' has no prior production assignment to restore.");

        ReleaseCommandRecord receipt = await ReserveAsync(
            releaseId,
            command,
            ReleaseOperation.Rollback,
            target,
            current,
            evidence: null,
            cancellationToken);
        before = await RequireRegistryAsync(command.RegistryId, cancellationToken);
        if (Matches(before.State.Production, receipt.Target))
        {
            return await RecoverAsync(receipt, before, replayed: true);
        }

        EnsureETag(command, before);
        RegistrySnapshot after;
        try
        {
            after = await registry.AssignProductionAsync(
                command.RegistryId,
                target.AgentId,
                target.PromptVersion,
                command.ExpectedETag,
                cancellationToken);
        }
        catch (RecordConflictException exception)
        {
            RegistrySnapshot concurrent = await RequireRegistryAsync(
                command.RegistryId,
                CancellationToken.None);
            if (Matches(concurrent.State.Production, receipt.Target))
            {
                return await RecoverAsync(receipt, concurrent, replayed: true);
            }

            throw new ReleaseConcurrencyException(exception.Message);
        }

        ReleaseRecord release = new(
            releaseId,
            command.RegistryId,
            command.RegistryId,
            command.IdempotencyKey,
            ReleaseOperation.Rollback,
            target.PromptVersion,
            target.AgentId,
            ReleaseLifecycle.RolledBack,
            command.Actor,
            DateTimeOffset.UtcNow,
            current,
            after.State.Production,
            target,
            GateEvidence: null,
            command.ExpectedETag,
            after.State,
            after.ETag);
        ReleaseRecord stored = await CreateReleaseIdempotentlyAsync(release, CancellationToken.None);
        return new ReleaseWorkflowResult(after, stored, Replayed: false);
    }

    private async Task<ReleaseGateEvidence> EvaluateGatesAsync(
        string registryId,
        string version,
        CancellationToken cancellationToken)
    {
        using GenAIOpsTelemetry.TelemetryOperation operation =
            GenAIOpsTelemetry.StartOperation("quality-gate.evaluate", "quality_gate", version);
        gates.Validate();
        MetricsPage page = await metrics.QueryAsync(
            new MetricsQuery(
                registryId,
                version,
                From: null,
                To: null,
                PageSize: 1,
                ContinuationToken: null),
            cancellationToken);
        MetricSnapshotRecord snapshot = page.Items.FirstOrDefault()
            ?? throw new ReleaseStateNotFoundException(
                $"No metric evidence exists for candidate version '{version}'.");
        List<string> reasons = [];
        CheckMinimum(snapshot, "taskAdherence", gates.MinimumTaskAdherence, reasons);
        CheckMinimum(snapshot, "groundedness", gates.MinimumGroundedness, reasons);
        CheckMinimum(snapshot, "toolAccuracy", gates.MinimumToolAccuracy, reasons);
        CheckMaximum(snapshot, "failureRate", gates.MaximumFailureRate, reasons);
        if (snapshot.SampleCount < gates.MinimumSamples)
        {
            reasons.Add(
                $"sampleCount {snapshot.SampleCount} is below {gates.MinimumSamples}");
        }

        if (gates.MaximumLatencyMilliseconds is { } maximumLatency)
        {
            CheckMaximum(snapshot, "latencyMilliseconds", maximumLatency, reasons);
        }

        ReleaseGateEvidence evidence = new(
            snapshot.Id,
            snapshot.WindowStart,
            snapshot.WindowEnd,
            snapshot.SampleCount,
            snapshot.Metrics,
            gates.ToDictionary(),
            reasons.Count == 0,
            reasons);
        operation.Complete(
            evidence.Passed ? "success" : "rejected",
            gateResult: evidence.Passed ? "passed" : "failed");
        return evidence;
    }

    private async Task<ReleaseRecord?> GetReplayAsync(
        string releaseId,
        ReleaseCommand command,
        ReleaseOperation operation,
        string? version,
        CancellationToken cancellationToken)
    {
        StoredItem<ReleaseRecord>? existing = await releases.GetAsync(
            releaseId,
            command.RegistryId,
            cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.Value.Operation != operation
            || version is not null
                && !string.Equals(existing.Value.PromptVersion, version, StringComparison.Ordinal)
            || !string.Equals(existing.Value.Actor, command.Actor, StringComparison.Ordinal)
            || !string.Equals(
                existing.Value.ExpectedETag,
                command.ExpectedETag,
                StringComparison.Ordinal))
        {
            throw new ReleaseIdempotencyConflictException(
                "The idempotency key was already used for a different release command.");
        }

        return existing.Value;
    }

    private async Task<ReleaseCommandRecord> ReserveAsync(
        string releaseId,
        ReleaseCommand command,
        ReleaseOperation operation,
        AgentAssignment target,
        AgentAssignment? previousProduction,
        ReleaseGateEvidence? evidence,
        CancellationToken cancellationToken)
    {
        ReleaseCommandRecord receipt = new(
            releaseId,
            command.RegistryId,
            command.RegistryId,
            command.IdempotencyKey,
            operation,
            operation == ReleaseOperation.Promotion ? target.PromptVersion : null,
            command.Actor,
            command.ExpectedETag,
            target,
            previousProduction,
            evidence,
            DateTimeOffset.UtcNow);
        try
        {
            return (await commands.CreateAsync(receipt, cancellationToken)).Value;
        }
        catch (RecordConflictException)
        {
            ReleaseCommandRecord existing = (await commands.GetAsync(
                releaseId,
                command.RegistryId,
                cancellationToken))?.Value
                ?? throw new ReleaseIdempotencyConflictException(
                    "The release command reservation could not be recovered.");
            if (existing.Operation != receipt.Operation
                || !string.Equals(existing.PromptVersion, receipt.PromptVersion, StringComparison.Ordinal)
                || !string.Equals(existing.Actor, receipt.Actor, StringComparison.Ordinal)
                || !string.Equals(existing.ExpectedETag, receipt.ExpectedETag, StringComparison.Ordinal))
            {
                throw new ReleaseIdempotencyConflictException(
                    "The idempotency key was already used for a different release command.");
            }

            return existing;
        }
    }

    private async Task<ReleaseWorkflowResult> RecoverAsync(
        ReleaseCommandRecord receipt,
        RegistrySnapshot current,
        bool replayed)
    {
        ReleaseRecord release = new(
            receipt.Id,
            receipt.PartitionKey,
            receipt.RegistryId,
            receipt.IdempotencyKey,
            receipt.Operation,
            receipt.Target.PromptVersion,
            receipt.Target.AgentId,
            receipt.Operation == ReleaseOperation.Promotion
                ? ReleaseLifecycle.Promoted
                : ReleaseLifecycle.RolledBack,
            receipt.Actor,
            receipt.CreatedAt,
            receipt.PreviousProduction,
            current.State.Production,
            receipt.Operation == ReleaseOperation.Rollback ? receipt.Target : receipt.PreviousProduction,
            receipt.GateEvidence,
            receipt.ExpectedETag,
            current.State,
            current.ETag);
        ReleaseRecord stored = await CreateReleaseIdempotentlyAsync(
            release,
            CancellationToken.None);
        return new ReleaseWorkflowResult(GetRecordedResult(stored), stored, replayed);
    }

    private static RegistrySnapshot GetRecordedResult(ReleaseRecord release)
    {
        if (release.ResultRegistry is null || release.ResultETag is null)
        {
            throw new ReleaseIdempotencyConflictException(
                "The stored release does not contain a completed routing result.");
        }

        return new RegistrySnapshot(release.ResultRegistry, release.ResultETag);
    }

    private static bool Matches(AgentAssignment? left, AgentAssignment right) =>
        left is not null
        && string.Equals(left.AgentId, right.AgentId, StringComparison.Ordinal)
        && string.Equals(left.PromptVersion, right.PromptVersion, StringComparison.Ordinal);

    private async Task<ReleaseRecord> CreateReleaseIdempotentlyAsync(
        ReleaseRecord release,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await releases.CreateAsync(release, cancellationToken)).Value;
        }
        catch (RecordConflictException)
        {
            StoredItem<ReleaseRecord>? existing = await releases.GetAsync(
                release.Id,
                release.PartitionKey,
                cancellationToken);
            return existing?.Value
                ?? throw new ReleaseIdempotencyConflictException(
                    "The release command conflicted and could not be recovered.");
        }
    }

    private async Task<RegistrySnapshot> RequireRegistryAsync(
        string registryId,
        CancellationToken cancellationToken) =>
        await registry.GetAsync(registryId, cancellationToken)
        ?? throw new ReleaseStateNotFoundException(
            $"Agent registry '{registryId}' was not found.");

    private static void EnsureETag(ReleaseCommand command, RegistrySnapshot snapshot)
    {
        if (!string.Equals(command.ExpectedETag, snapshot.ETag, StringComparison.Ordinal))
        {
            throw new ReleaseConcurrencyException("The supplied registry ETag is stale.");
        }
    }

    private static void Validate(string? version, ReleaseCommand command)
    {
        if (version is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
        }

        if (string.IsNullOrWhiteSpace(command.RegistryId)
            || string.IsNullOrWhiteSpace(command.Actor)
            || string.IsNullOrWhiteSpace(command.IdempotencyKey)
            || string.IsNullOrWhiteSpace(command.ExpectedETag))
        {
            throw new ReleaseValidationException(
                "Registry ID, actor, idempotency key, and If-Match ETag are required.");
        }

        if (command.Actor.Length > 200 || command.IdempotencyKey.Length > 200)
        {
            throw new ReleaseValidationException(
                "Actor and idempotency key must not exceed 200 characters.");
        }
    }

    private static void CheckMinimum(
        MetricSnapshotRecord snapshot,
        string name,
        double threshold,
        ICollection<string> reasons)
    {
        if (!snapshot.Metrics.TryGetValue(name, out double observed) || observed < threshold)
        {
            reasons.Add($"{name} {observed:0.####} is below {threshold:0.####}");
        }
    }

    private static void CheckMaximum(
        MetricSnapshotRecord snapshot,
        string name,
        double threshold,
        ICollection<string> reasons)
    {
        if (!snapshot.Metrics.TryGetValue(name, out double observed) || observed > threshold)
        {
            reasons.Add($"{name} {observed:0.####} exceeds {threshold:0.####}");
        }
    }

    private static string CreateReleaseId(string registryId, string idempotencyKey)
    {
        string source = $"{registryId}\n{idempotencyKey}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return $"release-{hash[..32].ToLowerInvariant()}";
    }
}
