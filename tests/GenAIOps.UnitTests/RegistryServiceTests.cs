using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Domain.Registry;
using GenAIOps.Infrastructure.Persistence;

namespace GenAIOps.UnitTests;

public sealed class RegistryServiceTests
{
    [Fact]
    public async Task Registry_retains_candidate_and_production_history_with_one_production()
    {
        AgentRegistryService service = CreateService();
        RegistrySnapshot v1Candidate =
            await service.RegisterCandidateAsync("support", "agent-v1", "v1");
        RegistrySnapshot v1Production =
            await service.PromoteCandidateAsync("support", "agent-v1", v1Candidate.ETag);
        RegistrySnapshot v2Candidate = await service.RegisterCandidateAsync(
            "support",
            "agent-v2",
            "v2",
            v1Production.ETag);
        RegistrySnapshot v2Production =
            await service.PromoteCandidateAsync("support", "agent-v2", v2Candidate.ETag);

        Assert.Equal("agent-v2", v2Production.State.Production?.AgentId);
        Assert.Null(v2Production.State.Candidate);
        Assert.Contains(
            v2Production.State.History,
            assignment => assignment.AgentId == "agent-v1"
                && assignment.Slot == AssignmentSlot.Production
                && assignment.UnassignedAt is not null);
        Assert.All(
            v2Production.State.History,
            assignment => Assert.NotNull(assignment.UnassignedAt));
    }

    [Fact]
    public async Task Registry_rejects_promotion_when_registry_is_not_found()
    {
        AgentRegistryService service = CreateService();

        await Assert.ThrowsAsync<RecordNotFoundException>(
            () => service.PromoteCandidateAsync("missing", "agent-v1", "\"etag\""));
    }

    [Fact]
    public async Task Registry_rejects_wrong_candidate_as_conflict()
    {
        AgentRegistryService service = CreateService();
        RegistrySnapshot candidate =
            await service.RegisterCandidateAsync("support", "agent-v1", "v1");

        await Assert.ThrowsAsync<RecordConflictException>(
            () => service.PromoteCandidateAsync("support", "agent-v2", candidate.ETag));
    }

    [Fact]
    public async Task Concurrent_registry_promotions_allow_only_one_ETag_winner()
    {
        AgentRegistryService service = CreateService();
        RegistrySnapshot candidate =
            await service.RegisterCandidateAsync("support", "agent-v1", "v1");

        Task<Exception?> first = Record.ExceptionAsync(
            () => Task.Run(
                () => service.PromoteCandidateAsync("support", "agent-v1", candidate.ETag)));
        Task<Exception?> second = Record.ExceptionAsync(
            () => Task.Run(
                () => service.PromoteCandidateAsync("support", "agent-v1", candidate.ETag)));
        Exception?[] results = await Task.WhenAll(first, second);

        Assert.Single(results, exception => exception is null);
        Assert.Single(results, exception => exception is RecordConflictException);
        RegistrySnapshot current = Assert.IsType<RegistrySnapshot>(await service.GetAsync("support"));
        Assert.Equal("agent-v1", current.State.Production?.AgentId);
    }

    private static AgentRegistryService CreateService() =>
        new(new InMemoryRepository<AgentRegistryState>());
}
