using GenAIOps.Application.Metrics;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Application.Releases;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;
using GenAIOps.Infrastructure.Persistence;

namespace GenAIOps.UnitTests;

public sealed class PromotionWorkflowTests
{
    [Fact]
    public async Task Promotion_v2_passes_configured_quality_gates_and_persists_evidence()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync("v2", 0.94, 0.97, 0.95);

        ReleaseWorkflowResult result = await fixture.Service.PromoteAsync(
            "v2",
            fixture.Command("promote-v2"));

        Assert.Equal("v2", result.Registry.State.Production?.PromptVersion);
        Assert.Equal(ReleaseLifecycle.Promoted, result.Release.Lifecycle);
        Assert.Equal("operator@example.com", result.Release.Actor);
        Assert.True(result.Release.GateEvidence?.Passed);
        Assert.Equal(3, result.Release.GateEvidence?.SampleCount);
        Assert.Equal(fixture.Snapshot.Id, result.Release.GateEvidence?.MetricSnapshotId);
        Assert.Equal("v1", result.Release.RollbackTarget?.PromptVersion);
    }

    [Fact]
    public async Task Promotion_v3_is_rejected_on_regression_and_routing_is_unchanged()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync("v3", 0.72, 0.75, 0.58);

        QualityGateRejectedException exception =
            await Assert.ThrowsAsync<QualityGateRejectedException>(
                () => fixture.Service.PromoteAsync(
                    "v3",
                    fixture.Command("reject-v3")));
        RegistrySnapshot registry = (await fixture.Registry.GetAsync("default"))!;
        RepositoryPage<ReleaseRecord> releases = await fixture.Releases.QueryAsync(
            new RecordQuery("default", "release"));

        Assert.Equal("quality_gate_rejected", exception.Code);
        Assert.False(exception.Evidence.Passed);
        Assert.Contains(
            exception.Evidence.Reasons,
            reason => reason.Contains("toolAccuracy", StringComparison.Ordinal));
        Assert.Equal("v1", registry.State.Production?.PromptVersion);
        Assert.Equal(ReleaseLifecycle.Rejected, Assert.Single(releases.Items).Value.Lifecycle);
    }

    [Fact]
    public async Task Promotion_requires_minimum_samples_and_metric_evidence()
    {
        WorkflowFixture insufficient = await WorkflowFixture.CreateAsync(
            "v2",
            0.94,
            0.97,
            0.95,
            sampleCount: 2);

        QualityGateRejectedException rejected =
            await Assert.ThrowsAsync<QualityGateRejectedException>(
                () => insufficient.Service.PromoteAsync(
                    "v2",
                    insufficient.Command("insufficient")));
        Assert.Contains(
            rejected.Evidence.Reasons,
            reason => reason.Contains("sampleCount", StringComparison.Ordinal));

        WorkflowFixture missing = await WorkflowFixture.CreateAsync(
            "v2",
            0.94,
            0.97,
            0.95,
            includeSnapshot: false);
        await Assert.ThrowsAsync<ReleaseStateNotFoundException>(
            () => missing.Service.PromoteAsync(
                "v2",
                missing.Command("missing-evidence")));
    }

    [Fact]
    public async Task Promotion_replay_is_idempotent_and_does_not_duplicate_release()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync("v2", 0.94, 0.97, 0.95);
        ReleaseCommand command = fixture.Command("same-promotion");

        ReleaseWorkflowResult first = await fixture.Service.PromoteAsync("v2", command);
        ReleaseWorkflowResult replay = await fixture.Service.PromoteAsync("v2", command);
        RepositoryPage<ReleaseRecord> releases = await fixture.Releases.QueryAsync(
            new RecordQuery("default", "release"));

        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Release.Id, replay.Release.Id);
        Assert.Single(releases.Items);
    }

    [Fact]
    public async Task Promotion_rejects_stale_etag_and_idempotency_key_reuse()
    {
        WorkflowFixture stale = await WorkflowFixture.CreateAsync("v2", 0.94, 0.97, 0.95);
        ReleaseConcurrencyException concurrency =
            await Assert.ThrowsAsync<ReleaseConcurrencyException>(
                () => stale.Service.PromoteAsync(
                    "v2",
                    stale.Command("stale", expectedETag: "\"stale\"")));
        Assert.Equal("stale_registry", concurrency.Code);

        WorkflowFixture reused = await WorkflowFixture.CreateAsync("v2", 0.94, 0.97, 0.95);
        ReleaseCommand command = reused.Command("reused");
        await reused.Service.PromoteAsync("v2", command);
        ReleaseIdempotencyConflictException idempotency =
            await Assert.ThrowsAsync<ReleaseIdempotencyConflictException>(
                () => reused.Service.RollbackAsync(command));
        Assert.Equal("idempotency_conflict", idempotency.Code);
    }

    [Fact]
    public async Task Concurrent_promotions_with_same_etag_have_exactly_one_winner()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync("v2", 0.94, 0.97, 0.95);

        Task<Exception?> first = Record.ExceptionAsync(
            () => fixture.Service.PromoteAsync("v2", fixture.Command("race-one")));
        Task<Exception?> second = Record.ExceptionAsync(
            () => fixture.Service.PromoteAsync("v2", fixture.Command("race-two")));
        Exception?[] outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, exception => exception is null);
        Assert.Single(
            outcomes,
            exception => exception is ReleaseConcurrencyException
                or ReleaseStateNotFoundException);
    }

    [Fact]
    public async Task Concurrent_identical_promotions_share_one_release()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync("v2", 0.94, 0.97, 0.95);
        ReleaseCommand command = fixture.Command("same-race");

        ReleaseWorkflowResult[] outcomes = await Task.WhenAll(
            fixture.Service.PromoteAsync("v2", command),
            fixture.Service.PromoteAsync("v2", command));

        Assert.Equal(outcomes[0].Release.Id, outcomes[1].Release.Id);
        Assert.Single(
            (await fixture.Releases.QueryAsync(new RecordQuery("default", "release"))).Items,
            item => item.Value.Id == outcomes[0].Release.Id);
    }

    [Fact]
    public async Task Replay_requires_exact_actor_and_returns_original_result()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync("v2", 0.94, 0.97, 0.95);
        ReleaseCommand command = fixture.Command("exact-replay");
        ReleaseWorkflowResult first = await fixture.Service.PromoteAsync("v2", command);
        await fixture.Registry.RegisterCandidateAsync(
            "default",
            "agent-v3",
            "v3",
            first.Registry.ETag);

        ReleaseWorkflowResult replay = await fixture.Service.PromoteAsync("v2", command);
        Assert.Equal(first.Registry.ETag, replay.Registry.ETag);
        Assert.Null(replay.Registry.State.Candidate);
        await Assert.ThrowsAsync<ReleaseIdempotencyConflictException>(
            () => fixture.Service.PromoteAsync(
                "v2",
                command with { Actor = "different@example.com" }));
    }
}

public sealed class RollbackWorkflowTests
{
    [Fact]
    public async Task Rollback_restores_v2_after_bad_v3_and_is_idempotent()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync("v2", 0.94, 0.97, 0.95);
        ReleaseWorkflowResult v2 = await fixture.Service.PromoteAsync(
            "v2",
            fixture.Command("promote-v2"));
        RegistrySnapshot v3Candidate = await fixture.Registry.RegisterCandidateAsync(
            "default",
            "agent-v3",
            "v3",
            v2.Registry.ETag);
        RegistrySnapshot badV3 = await fixture.Registry.PromoteCandidateAsync(
            "default",
            "agent-v3",
            v3Candidate.ETag);
        ReleaseCommand rollbackCommand = fixture.Command(
            "rollback-v3",
            badV3.ETag);

        ReleaseWorkflowResult rollback =
            await fixture.Service.RollbackAsync(rollbackCommand);
        ReleaseWorkflowResult replay =
            await fixture.Service.RollbackAsync(rollbackCommand);
        RepositoryPage<ReleaseRecord> releases = await fixture.Releases.QueryAsync(
            new RecordQuery("default", "release"));

        Assert.Equal("v2", rollback.Registry.State.Production?.PromptVersion);
        Assert.Equal("v2", rollback.Release.RollbackTarget?.PromptVersion);
        Assert.Equal("v3", rollback.Release.PreviousProduction?.PromptVersion);
        Assert.Equal(ReleaseLifecycle.RolledBack, rollback.Release.Lifecycle);
        Assert.True(replay.Replayed);
        Assert.Equal(2, releases.Items.Count);
    }

    [Fact]
    public async Task Rollback_requires_prior_production_and_current_etag()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync("v2", 0.94, 0.97, 0.95);

        await Assert.ThrowsAsync<ReleaseConcurrencyException>(
            () => fixture.Service.RollbackAsync(
                fixture.Command("stale-rollback", "\"stale\"")));
        await Assert.ThrowsAsync<ReleaseStateNotFoundException>(
            () => fixture.Service.RollbackAsync(
                fixture.Command("no-history")));
    }
}

internal sealed class WorkflowFixture
{
    private WorkflowFixture(
        AgentRegistryService registry,
        InMemoryRepository<ReleaseRecord> releases,
        ReleaseWorkflowService service,
        RegistrySnapshot snapshot,
        MetricSnapshotRecord metricSnapshot)
    {
        Registry = registry;
        Releases = releases;
        Service = service;
        Snapshot = metricSnapshot;
        RegistrySnapshot = snapshot;
    }

    public AgentRegistryService Registry { get; }

    public InMemoryRepository<ReleaseRecord> Releases { get; }

    public ReleaseWorkflowService Service { get; }

    public MetricSnapshotRecord Snapshot { get; }

    public RegistrySnapshot RegistrySnapshot { get; }

    public ReleaseCommand Command(
        string idempotencyKey,
        string? expectedETag = null) =>
        new(
            "default",
            "operator@example.com",
            idempotencyKey,
            expectedETag ?? RegistrySnapshot.ETag);

    public static async Task<WorkflowFixture> CreateAsync(
        string candidateVersion,
        double taskAdherence,
        double groundedness,
        double toolAccuracy,
        int sampleCount = 3,
        bool includeSnapshot = true)
    {
        AgentRegistryService registry =
            new(new InMemoryRepository<AgentRegistryState>());
        RegistrySnapshot initial =
            await registry.RegisterCandidateAsync("default", "agent-v1", "v1");
        RegistrySnapshot production =
            await registry.PromoteCandidateAsync("default", "agent-v1", initial.ETag);
        RegistrySnapshot candidate = await registry.RegisterCandidateAsync(
            "default",
            $"agent-{candidateVersion}",
            candidateVersion,
            production.ETag);
        InMemoryRepository<MetricSnapshotRecord> metricsRepository = new();
        MetricSnapshotRecord snapshot = new(
            $"metrics-{candidateVersion}",
            "default",
            candidateVersion,
            DateTimeOffset.Parse("2026-09-22T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-23T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-23T00:01:00Z"),
            sampleCount,
            sampleCount,
            FailureCount: 0,
            Enumerable.Range(1, sampleCount).Select(index => $"evaluation-{index}").ToArray(),
            new Dictionary<string, double>
            {
                ["taskAdherence"] = taskAdherence,
                ["groundedness"] = groundedness,
                ["toolAccuracy"] = toolAccuracy,
                ["failureRate"] = 0,
                ["latencyMilliseconds"] = 100,
                ["sampleCount"] = sampleCount,
            });
        if (includeSnapshot)
        {
            await metricsRepository.CreateAsync(snapshot);
        }

        InMemoryRepository<ReleaseRecord> releases = new();
        ReleaseWorkflowService service = new(
            registry,
            new MetricsQueryService(metricsRepository),
            releases,
            new InMemoryRepository<ReleaseCommandRecord>(),
            new QualityGateOptions(
                MinimumSamples: 3,
                MinimumTaskAdherence: 0.9,
                MinimumGroundedness: 0.9,
                MinimumToolAccuracy: 0.9,
                MaximumFailureRate: 0.05,
                MaximumLatencyMilliseconds: 1000));
        return new WorkflowFixture(registry, releases, service, candidate, snapshot);
    }
}
