using GenAIOps.Application.Experiments;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Domain.Records;
using GenAIOps.Infrastructure.Persistence;

namespace GenAIOps.UnitTests;

public sealed class ExperimentRoutingTests
{
    [Fact]
    public async Task Deterministic_assignment_is_sticky_and_matches_90_10_tolerance()
    {
        ExperimentFixture fixture = await ExperimentFixture.CreateAsync();
        await fixture.StartAsync();

        ExperimentAssignment first = await fixture.Router.AssignAsync(
            "default",
            fixture.Registry,
            "stable-user");
        ExperimentAssignment replay = await fixture.Router.AssignAsync(
            "default",
            fixture.Registry,
            "stable-user");
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        for (int index = 0; index < 20_000; index++)
        {
            ExperimentAssignment assignment = await fixture.Router.AssignAsync(
                "default",
                fixture.Registry,
                $"user-{index}");
            counts[assignment.PromptVersion] =
                counts.GetValueOrDefault(assignment.PromptVersion) + 1;
        }

        Assert.Equal(first.PromptVersion, replay.PromptVersion);
        Assert.Equal(first.ExperimentId, replay.ExperimentId);
        Assert.InRange(counts["v2"] / 20000d, 0.88, 0.92);
        Assert.InRange(counts["v1"] / 20000d, 0.08, 0.12);
    }

    [Fact]
    public async Task Lifecycle_update_uses_etag_and_end_restores_production_routing()
    {
        ExperimentFixture fixture = await ExperimentFixture.CreateAsync();
        ExperimentResult started = await fixture.StartAsync();
        ExperimentResult updated = await fixture.Service.ExecuteAsync(
            fixture.Command(
                ExperimentCommandAction.Update,
                [new("v2", 80), new("v1", 20)],
                started.ETag));

        await Assert.ThrowsAsync<ExperimentConcurrencyException>(
            () => fixture.Service.ExecuteAsync(
                fixture.Command(
                    ExperimentCommandAction.End,
                    allocations: null,
                    expectedETag: started.ETag)));
        ExperimentResult ended = await fixture.Service.ExecuteAsync(
            fixture.Command(
                ExperimentCommandAction.End,
                allocations: null,
                expectedETag: updated.ETag));
        ExperimentAssignment normal = await fixture.Router.AssignAsync(
            "default",
            fixture.Registry,
            assignmentKey: null);

        Assert.Equal(ExperimentLifecycle.Completed, ended.Experiment.Lifecycle);
        Assert.NotNull(ended.Experiment.EndedAt);
        Assert.Equal("v2", normal.PromptVersion);
        Assert.Null(normal.ExperimentId);
        await Assert.ThrowsAsync<ExperimentLifecycleException>(
            () => fixture.Service.ExecuteAsync(
                fixture.Command(
                    ExperimentCommandAction.Update,
                    [new("v2", 90), new("v1", 10)],
                    ended.ETag)));
        ExperimentResult restarted = await fixture.Service.ExecuteAsync(
            fixture.Command(
                ExperimentCommandAction.Start,
                [new("v2", 90), new("v1", 10)]));
        Assert.NotEqual(ended.Experiment.ExperimentId, restarted.Experiment.ExperimentId);
    }

    [Theory]
    [MemberData(nameof(InvalidAllocations))]
    public async Task Allocation_validation_rejects_invalid_totals(
        IReadOnlyList<ExperimentAllocation> allocations)
    {
        ExperimentFixture fixture = await ExperimentFixture.CreateAsync();

        await Assert.ThrowsAsync<ExperimentValidationException>(
            () => fixture.Service.ExecuteAsync(
                fixture.Command(ExperimentCommandAction.Start, allocations)));
    }

    [Fact]
    public async Task Eligibility_rejects_candidate_only_and_unknown_versions()
    {
        ExperimentFixture fixture = await ExperimentFixture.CreateAsync(
            candidateVersion: "v3");

        await Assert.ThrowsAsync<ExperimentEligibilityException>(
            () => fixture.Service.ExecuteAsync(
                fixture.Command(
                    ExperimentCommandAction.Start,
                    [new("v2", 90), new("v3", 10)])));
        await Assert.ThrowsAsync<ExperimentEligibilityException>(
            () => fixture.Service.ExecuteAsync(
                fixture.Command(
                    ExperimentCommandAction.Start,
                    [new("v2", 90), new("v9", 10)])));
    }

    [Fact]
    public async Task Running_experiment_requires_an_approved_assignment_key()
    {
        ExperimentFixture fixture = await ExperimentFixture.CreateAsync();
        await fixture.StartAsync();

        await Assert.ThrowsAsync<GenAIOps.Application.Chat.ChatValidationException>(
            () => fixture.Router.AssignAsync("default", fixture.Registry, assignmentKey: null));
        await Assert.ThrowsAsync<GenAIOps.Application.Chat.ChatValidationException>(
            () => fixture.Router.AssignAsync(
                "default",
                fixture.Registry,
                new string('x', ExperimentRouter.MaximumAssignmentKeyLength + 1)));
    }

    public static TheoryData<IReadOnlyList<ExperimentAllocation>> InvalidAllocations() =>
        new()
        {
            { new[] { new ExperimentAllocation("v2", 90), new("v1", 9) } },
            { new[] { new ExperimentAllocation("v2", 100) } },
            { new[] { new ExperimentAllocation("v2", 90), new("v2", 10) } },
            { new[] { new ExperimentAllocation("v2", 110), new("v1", -10) } },
        };
}

internal sealed class ExperimentFixture
{
    private ExperimentFixture(
        ExperimentService service,
        ExperimentRouter router,
        RegistrySnapshot registry)
    {
        Service = service;
        Router = router;
        Registry = registry;
    }

    public ExperimentService Service { get; }

    public ExperimentRouter Router { get; }

    public RegistrySnapshot Registry { get; }

    public static async Task<ExperimentFixture> CreateAsync(string? candidateVersion = null)
    {
        InMemoryRepository<GenAIOps.Domain.Registry.AgentRegistryState> registryRepository = new();
        AgentRegistryService registry = new(registryRepository);
        RegistrySnapshot v1 =
            await registry.RegisterCandidateAsync("default", "agent-v1", "v1");
        RegistrySnapshot productionV1 =
            await registry.PromoteCandidateAsync("default", "agent-v1", v1.ETag);
        RegistrySnapshot v2 = await registry.RegisterCandidateAsync(
            "default",
            "agent-v2",
            "v2",
            productionV1.ETag);
        RegistrySnapshot productionV2 =
            await registry.PromoteCandidateAsync("default", "agent-v2", v2.ETag);
        if (candidateVersion is not null)
        {
            productionV2 = await registry.RegisterCandidateAsync(
                "default",
                $"agent-{candidateVersion}",
                candidateVersion,
                productionV2.ETag);
        }

        InMemoryRepository<ExperimentRecord> experiments = new();
        return new ExperimentFixture(
            new ExperimentService(experiments, registry),
            new ExperimentRouter(experiments),
            productionV2);
    }

    public Task<ExperimentResult> StartAsync() =>
        Service.ExecuteAsync(
            Command(
                ExperimentCommandAction.Start,
                [new("v2", 90), new("v1", 10)]));

    public ExperimentCommand Command(
        ExperimentCommandAction action,
        IReadOnlyList<ExperimentAllocation>? allocations,
        string? expectedETag = null) =>
        new(
            "default",
            action,
            "unit-test",
            "v2-v1",
            allocations,
            expectedETag);
}
