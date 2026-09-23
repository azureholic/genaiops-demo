using GenAIOps.Application.Chat;
using GenAIOps.Application.Experiments;
using GenAIOps.Application.Metrics;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Application.Releases;
using GenAIOps.Application.Shadow;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;
using GenAIOps.Infrastructure.Chat;
using GenAIOps.Infrastructure.Persistence;

namespace GenAIOps.Demo;

public sealed record DemoPhase(int Number, string Name, string Evidence);

public sealed record DemoReport(
    string DataSource,
    IReadOnlyList<DemoPhase> Phases,
    string FinalProductionVersion,
    long ElapsedMilliseconds = 0);

public static class DemoScenario
{
    private const string RegistryId = "deterministic-demo";
    private const string Actor = "demo-operator";
    private static readonly DateTimeOffset WindowStart =
        DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static readonly DateTimeOffset WindowEnd =
        DateTimeOffset.Parse("2027-01-01T00:00:00Z");

    public static async Task<DemoReport> RunAsync(CancellationToken cancellationToken = default)
    {
        InMemoryRepository<AgentRegistryState> registryRepository = new();
        InMemoryRepository<ShadowEvaluationRecord> evaluationRepository = new();
        InMemoryRepository<MetricSnapshotRecord> metricRepository = new();
        InMemoryRepository<ReleaseRecord> releaseRepository = new();
        InMemoryRepository<ReleaseCommandRecord> releaseCommandRepository = new();
        InMemoryRepository<ExperimentRecord> experimentRepository = new();
        InMemoryRepository<ChatRequestMetadataRecord> metadataRepository = new();

        AgentRegistryService registry = new(registryRepository);
        CollectingShadowPublisher shadowPublisher = new();
        FakeChatGateway gateway = new();
        ExperimentRouter router = new(experimentRepository);
        ChatService chat = new(
            registry,
            gateway,
            metadataRepository,
            shadowPublisher,
            router);
        ShadowEvaluationProcessor shadowProcessor = new(
            gateway,
            new VersionedDemoEvaluator(),
            evaluationRepository,
            ShadowProcessingOptions.Default);
        MetricsAggregator aggregator = new(evaluationRepository, metricRepository);
        ReleaseWorkflowService releases = new(
            registry,
            new MetricsQueryService(metricRepository),
            releaseRepository,
            releaseCommandRepository,
            new QualityGateOptions(
                MinimumSamples: 3,
                MinimumTaskAdherence: 0.9,
                MinimumGroundedness: 0.9,
                MinimumToolAccuracy: 0.9,
                MaximumFailureRate: 0.05,
                MaximumLatencyMilliseconds: 1_000));
        ExperimentService experiments = new(experimentRepository, registry);
        List<DemoPhase> phases = [];

        RegistrySnapshot v1Candidate = await registry.RegisterCandidateAsync(
            RegistryId,
            "demo-agent-v1",
            "v1",
            cancellationToken: cancellationToken);
        RegistrySnapshot v1Production = await registry.PromoteCandidateAsync(
            RegistryId,
            "demo-agent-v1",
            v1Candidate.ETag,
            cancellationToken);
        ChatResponse baseline = await chat.SendAsync(
            RegistryId,
            "Check order status",
            "demo-v1-baseline",
            cancellationToken);
        Require(baseline.AssignedPromptVersion == "v1", "The baseline did not route to v1.");
        phases.Add(new DemoPhase(1, "v1 baseline", "visible response routed to v1"));

        RegistrySnapshot v2Candidate = await registry.RegisterCandidateAsync(
            RegistryId,
            "demo-agent-v2",
            "v2",
            v1Production.ETag,
            cancellationToken);
        Require(
            v2Candidate.State.Production?.PromptVersion == "v1"
                && v2Candidate.State.Candidate?.PromptVersion == "v2",
            "v2 registration changed the production assignment.");
        phases.Add(new DemoPhase(2, "deploy/register v2", "v1 production; v2 candidate"));

        await GenerateAndProcessShadowSamplesAsync(
            chat,
            shadowPublisher,
            shadowProcessor,
            "v2",
            cancellationToken);
        IReadOnlyList<MetricSnapshotRecord> v2Metrics = await aggregator.AggregateAsync(
            new AggregationWindow(RegistryId, WindowStart, WindowEnd),
            cancellationToken);
        MetricSnapshotRecord v2Snapshot =
            v2Metrics.Single(snapshot => snapshot.PromptVersion == "v2");
        Require(
            v2Snapshot.SampleCount == 3
                && v2Snapshot.Metrics["taskAdherence"] >= 0.9,
            "v2 shadow evidence was not deterministic or did not pass.");
        phases.Add(
            new DemoPhase(
                3,
                "hidden v2 shadow evaluation",
                "3 hidden samples; quality gate evidence passes"));

        RegistrySnapshot beforeV2Promotion =
            await RequireRegistryAsync(registry, cancellationToken);
        ReleaseWorkflowResult v2Promotion = await releases.PromoteAsync(
            "v2",
            new ReleaseCommand(
                RegistryId,
                Actor,
                "demo-promote-v2",
                beforeV2Promotion.ETag),
            cancellationToken);
        Require(
            v2Promotion.Registry.State.Production?.PromptVersion == "v2",
            "v2 was not promoted.");
        phases.Add(new DemoPhase(4, "promote v2", "quality gates passed; production=v2"));

        ExperimentResult experiment = await experiments.ExecuteAsync(
            new ExperimentCommand(
                RegistryId,
                ExperimentCommandAction.Start,
                Actor,
                "deterministic-90-10",
                [new("v2", 90), new("v1", 10)],
                ExpectedETag: null),
            cancellationToken);
        string v2Key = FindAssignmentKey(experiment.Experiment.ExperimentId, useV2: true);
        string v1Key = FindAssignmentKey(experiment.Experiment.ExperimentId, useV2: false);
        ChatResponse v2Route = await chat.SendAsync(
            RegistryId,
            "A/B v2 route",
            "demo-ab-v2",
            cancellationToken,
            v2Key);
        ChatResponse v1Route = await chat.SendAsync(
            RegistryId,
            "A/B v1 route",
            "demo-ab-v1",
            cancellationToken,
            v1Key);
        Require(
            v2Route.AssignedPromptVersion == "v2"
                && v1Route.AssignedPromptVersion == "v1",
            "The deterministic A/B routing probes did not cover both allocations.");
        phases.Add(new DemoPhase(5, "90/10 v2/v1 A/B", "allocation=90/10; both routes probed"));

        RegistrySnapshot beforeV3 =
            await RequireRegistryAsync(registry, cancellationToken);
        await registry.RegisterCandidateAsync(
            RegistryId,
            "demo-agent-v3",
            "v3",
            beforeV3.ETag,
            cancellationToken);
        await GenerateAndProcessShadowSamplesAsync(
            chat,
            shadowPublisher,
            shadowProcessor,
            "v3",
            cancellationToken,
            v2Key);
        IReadOnlyList<MetricSnapshotRecord> v3Metrics = await aggregator.AggregateAsync(
            new AggregationWindow(RegistryId, WindowStart, WindowEnd),
            cancellationToken);
        MetricSnapshotRecord v3Snapshot =
            v3Metrics.Single(snapshot => snapshot.PromptVersion == "v3");
        RegistrySnapshot beforeRejectedPromotion =
            await RequireRegistryAsync(registry, cancellationToken);
        QualityGateRejectedException rejection;
        try
        {
            await releases.PromoteAsync(
                "v3",
                new ReleaseCommand(
                    RegistryId,
                    Actor,
                    "demo-reject-v3",
                    beforeRejectedPromotion.ETag),
                cancellationToken);
            throw new InvalidOperationException("The intentionally poor v3 unexpectedly passed.");
        }
        catch (QualityGateRejectedException exception)
        {
            rejection = exception;
        }

        Require(
            !rejection.Evidence.Passed
                && v3Snapshot.Metrics["toolAccuracy"] < 0.9
                && (await RequireRegistryAsync(registry, cancellationToken))
                    .State.Production?.PromptVersion == "v2",
            "v3 regression did not fail safely.");
        phases.Add(
            new DemoPhase(
                6,
                "v3 regression",
                "3 poor shadow samples; promotion gate rejected; production remains v2"));

        await experiments.ExecuteAsync(
            new ExperimentCommand(
                RegistryId,
                ExperimentCommandAction.End,
                Actor,
                Name: null,
                Allocations: null,
                experiment.ETag),
            cancellationToken);
        RegistrySnapshot safeState = await RequireRegistryAsync(registry, cancellationToken);
        RegistrySnapshot incident = await registry.AssignProductionAsync(
            RegistryId,
            "demo-agent-v3",
            "v3",
            safeState.ETag,
            cancellationToken);
        ReleaseWorkflowResult rollback = await releases.RollbackAsync(
            new ReleaseCommand(
                RegistryId,
                Actor,
                "demo-rollback-v3",
                incident.ETag),
            cancellationToken);
        Require(
            rollback.Registry.State.Production?.PromptVersion == "v2",
            "Rollback did not restore v2.");
        phases.Add(
            new DemoPhase(
                7,
                "rollback to v2",
                "isolated incident fixture set v3; release history restored v2"));

        return new DemoReport(
            "deterministic in-memory fixture (never live operational state)",
            phases,
            rollback.Registry.State.Production!.PromptVersion);
    }

    private static async Task GenerateAndProcessShadowSamplesAsync(
        ChatService chat,
        CollectingShadowPublisher publisher,
        ShadowEvaluationProcessor processor,
        string candidateVersion,
        CancellationToken cancellationToken,
        string? assignmentKey = null)
    {
        publisher.Clear();
        for (int index = 1; index <= 3; index++)
        {
            ChatResponse visible = await chat.SendAsync(
                RegistryId,
                $"Deterministic sample {index}",
                $"demo-{candidateVersion}-shadow-{index}",
                cancellationToken,
                assignmentKey);
            Require(
                !visible.Message.Contains(
                    $"demo-agent-{candidateVersion}/{candidateVersion}",
                    StringComparison.Ordinal),
                $"Candidate {candidateVersion} leaked into the visible response.");
        }

        Require(publisher.Items.Count == 3, $"Expected three {candidateVersion} shadow items.");
        foreach (ShadowWorkItem item in publisher.Items)
        {
            ShadowProcessingResult result = await processor.ProcessAsync(
                new ShadowWorkDelivery(item, Attempt: 1),
                cancellationToken);
            Require(
                result.Disposition == ShadowProcessingDisposition.Completed,
                $"Shadow sample {item.CorrelationId} did not complete.");
        }
    }

    private static async Task<RegistrySnapshot> RequireRegistryAsync(
        AgentRegistryService registry,
        CancellationToken cancellationToken) =>
        await registry.GetAsync(RegistryId, cancellationToken)
            ?? throw new InvalidOperationException("The demo registry was not found.");

    private static string FindAssignmentKey(string experimentId, bool useV2)
    {
        for (int index = 0; index < 10_000; index++)
        {
            string key = $"demo-assignment-{index}";
            int bucket = ExperimentRouter.GetBucket(experimentId, key);
            if (useV2 ? bucket < 90 : bucket >= 90)
            {
                return key;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic assignment key.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CollectingShadowPublisher : IShadowWorkPublisher
    {
        public List<ShadowWorkItem> Items { get; } = [];

        public bool TryPublish(ShadowWorkItem work)
        {
            Items.Add(work);
            return true;
        }

        public void Clear() => Items.Clear();
    }

    private sealed class VersionedDemoEvaluator : IResponseEvaluator
    {
        public Task<EvaluationScores> EvaluateAsync(
            EvaluationInput input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool poor = input.CandidateResponse.Message.Contains(
                "/v3:",
                StringComparison.Ordinal);
            return Task.FromResult(
                poor
                    ? new EvaluationScores(0.52, 0.48, 0.35)
                    : new EvaluationScores(0.96, 0.97, 0.95));
        }
    }
}
