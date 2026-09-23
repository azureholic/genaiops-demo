using GenAIOps.Application.Persistence;
using GenAIOps.Application.Shadow;
using GenAIOps.Domain.Records;

namespace GenAIOps.Infrastructure.Shadow;

public sealed class PersistentShadowWorkQueue(
    IRepository<ShadowWorkRecord> repository) : IShadowWorkQueue
{
    private const string QueuePartition = "shadow-work";

    public bool TryPublish(ShadowWorkItem work)
    {
        try
        {
            repository.CreateAsync(ToRecord(work)).GetAwaiter().GetResult();
            return true;
        }
        catch (RecordConflictException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<ShadowWorkDelivery> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            RepositoryPage<ShadowWorkRecord> page = await repository.QueryAsync(
                new RecordQuery(QueuePartition, "shadowWork", PageSize: 100),
                cancellationToken);
            foreach (StoredItem<ShadowWorkRecord> item in page.Items)
            {
                if (item.Value.Lifecycle != ShadowWorkLifecycle.Pending)
                {
                    continue;
                }

                try
                {
                    ShadowWorkRecord claimed = item.Value with
                    {
                        Lifecycle = ShadowWorkLifecycle.Processing,
                    };
                    StoredItem<ShadowWorkRecord> stored = await repository.ReplaceAsync(
                        claimed,
                        item.ETag,
                        cancellationToken);
                    return new ShadowWorkDelivery(
                        ToWork(stored.Value),
                        stored.Value.AttemptCount,
                        stored.ETag);
                }
                catch (RecordConflictException)
                {
                    // Another worker claimed this delivery.
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    public void Complete(ShadowWorkDelivery delivery) =>
        Transition(delivery, ShadowWorkLifecycle.Completed, delivery.Attempt, null);

    public void Retry(ShadowWorkDelivery delivery) =>
        Transition(delivery, ShadowWorkLifecycle.Pending, delivery.Attempt + 1, null);

    public void DeadLetter(ShadowWorkDelivery delivery, string reason) =>
        Transition(delivery, ShadowWorkLifecycle.DeadLettered, delivery.Attempt, reason);

    private void Transition(
        ShadowWorkDelivery delivery,
        ShadowWorkLifecycle lifecycle,
        int attempt,
        string? reason)
    {
        if (delivery.Receipt is null)
        {
            throw new InvalidOperationException("A persistent queue receipt is required.");
        }

        ShadowWorkRecord record = ToRecord(delivery.Work) with
        {
            Lifecycle = lifecycle,
            AttemptCount = attempt,
            DeadLetterReason = reason,
        };
        repository.ReplaceAsync(record, delivery.Receipt).GetAwaiter().GetResult();
    }

    private static ShadowWorkRecord ToRecord(ShadowWorkItem work) =>
        new(
            work.CorrelationId,
            QueuePartition,
            work.CorrelationId,
            work.RegistryId,
            work.ProductionAgentId,
            work.ProductionPromptVersion,
            work.CandidateAgentId,
            work.CandidatePromptVersion,
            work.Input,
            work.ProductionOutput,
            work.PublishedAt,
            ShadowWorkLifecycle.Pending,
            AttemptCount: 1,
            DeadLetterReason: null,
            work.ExperimentId,
            work.AssignedPromptVersion);

    private static ShadowWorkItem ToWork(ShadowWorkRecord record) =>
        new(
            record.CorrelationId,
            record.RegistryId,
            record.ProductionAgentId,
            record.ProductionPromptVersion,
            record.CandidateAgentId,
            record.CandidatePromptVersion,
            record.Input,
            record.ProductionOutput,
            record.PublishedAt,
            record.ExperimentId,
            record.AssignedPromptVersion);
}
