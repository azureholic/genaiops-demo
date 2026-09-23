using System.Diagnostics;
using GenAIOps.Application.Chat;
using GenAIOps.Application.Persistence;
using GenAIOps.Domain.Records;

namespace GenAIOps.Application.Shadow;

public sealed record ShadowProcessingOptions(
    TimeSpan OperationTimeout,
    int MaximumAttempts)
{
    public static ShadowProcessingOptions Default { get; } =
        new(TimeSpan.FromSeconds(30), MaximumAttempts: 3);

    public void Validate()
    {
        if (OperationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(OperationTimeout));
        }

        if (MaximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAttempts));
        }
    }
}

public sealed class ShadowEvaluationProcessor(
    IChatGateway candidateGateway,
    IResponseEvaluator evaluator,
    IRepository<ShadowEvaluationRecord> repository,
    ShadowProcessingOptions options) : IShadowEvaluationProcessor
{
    public async Task<ShadowProcessingResult> ProcessAsync(
        ShadowWorkDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        ShadowWorkItem work = delivery.Work;
        StoredItem<ShadowEvaluationRecord>? existing =
            await repository.GetAsync(work.CorrelationId, work.RegistryId, cancellationToken);
        if (existing is not null
            && (existing.Value.Lifecycle is EvaluationLifecycle.Completed
                or EvaluationLifecycle.Failed
                or EvaluationLifecycle.Poisoned
                || existing.Value.AttemptCount >= delivery.Attempt))
        {
            return new ShadowProcessingResult(ShadowProcessingDisposition.Duplicate);
        }

        StoredItem<ShadowEvaluationRecord> claim;
        try
        {
            ShadowEvaluationRecord running = CreateRecord(
                work,
                existing?.Value.CandidateOutput,
                EvaluationLifecycle.Running,
                existing?.Value.Scores ?? new Dictionary<string, double>(),
                existing?.Value.CandidateLatencyMilliseconds,
                delivery.Attempt,
                null,
                null,
                completedAt: null);
            claim = existing is null
                ? await repository.CreateAsync(running, cancellationToken)
                : await repository.ReplaceAsync(running, existing.ETag, cancellationToken);
        }
        catch (RecordConflictException)
        {
            return new ShadowProcessingResult(ShadowProcessingDisposition.Duplicate);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string? candidateOutput = null;
        try
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.OperationTimeout);
            ChatGatewayResponse candidate = await candidateGateway.SendAsync(
                new ChatGatewayRequest(
                    work.CandidateAgentId,
                    work.CandidatePromptVersion,
                    work.Input,
                    work.CorrelationId),
                timeout.Token);
            candidateOutput = candidate.Message;
            stopwatch.Stop();
            EvaluationScores scores = await evaluator.EvaluateAsync(
                new EvaluationInput(work.Input, work.ProductionOutput, candidate),
                timeout.Token);
            await repository.ReplaceAsync(
                CreateRecord(
                    work,
                    candidateOutput,
                    EvaluationLifecycle.Completed,
                    scores.ToDictionary(),
                    stopwatch.ElapsedMilliseconds,
                    delivery.Attempt,
                    null,
                    null,
                    DateTimeOffset.UtcNow),
                claim.ETag,
                cancellationToken);
            return new ShadowProcessingResult(ShadowProcessingDisposition.Completed);
        }
        catch (Exception exception) when (
            exception is ChatProviderException
                or OperationCanceledException
                or TimeoutException)
        {
            string code = exception is OperationCanceledException
                ? "shadow_timeout"
                : "shadow_provider_failure";
            if (delivery.Attempt < options.MaximumAttempts)
            {
                await repository.ReplaceAsync(
                    CreateRecord(
                        work,
                        candidateOutput,
                        EvaluationLifecycle.Pending,
                        new Dictionary<string, double>(),
                        stopwatch.ElapsedMilliseconds,
                        delivery.Attempt,
                        code,
                        "Shadow processing will be retried.",
                        completedAt: null),
                    claim.ETag,
                    CancellationToken.None);
                return new ShadowProcessingResult(ShadowProcessingDisposition.Retry, code);
            }

            await repository.ReplaceAsync(
                CreateRecord(
                    work,
                    candidateOutput,
                    EvaluationLifecycle.Poisoned,
                    new Dictionary<string, double>(),
                    stopwatch.ElapsedMilliseconds,
                    delivery.Attempt,
                    code,
                    "Shadow processing failed after the maximum number of attempts.",
                    DateTimeOffset.UtcNow),
                claim.ETag,
                CancellationToken.None);
            return new ShadowProcessingResult(ShadowProcessingDisposition.Poisoned, code);
        }
        catch (Exception exception)
        {
            const string code = "evaluation_failure";
            if (delivery.Attempt < options.MaximumAttempts)
            {
                await repository.ReplaceAsync(
                    CreateRecord(
                        work,
                        candidateOutput,
                        EvaluationLifecycle.Pending,
                        new Dictionary<string, double>(),
                        stopwatch.ElapsedMilliseconds,
                        delivery.Attempt,
                        code,
                        "Evaluation will be retried.",
                        completedAt: null),
                    claim.ETag,
                    CancellationToken.None);
                return new ShadowProcessingResult(ShadowProcessingDisposition.Retry, code);
            }

            await repository.ReplaceAsync(
                CreateRecord(
                    work,
                    candidateOutput,
                    EvaluationLifecycle.Failed,
                    new Dictionary<string, double>(),
                    stopwatch.ElapsedMilliseconds,
                    delivery.Attempt,
                    code,
                    exception.Message,
                    DateTimeOffset.UtcNow),
                claim.ETag,
                CancellationToken.None);
            return new ShadowProcessingResult(ShadowProcessingDisposition.Poisoned, code);
        }
    }

    private static ShadowEvaluationRecord CreateRecord(
        ShadowWorkItem work,
        string? candidateOutput,
        EvaluationLifecycle lifecycle,
        IReadOnlyDictionary<string, double> scores,
        long? latency,
        int attempt,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset? completedAt) =>
        new(
            work.CorrelationId,
            work.RegistryId,
            work.CorrelationId,
            work.RegistryId,
            work.ProductionAgentId,
            work.ProductionPromptVersion,
            work.CandidateAgentId,
            work.CandidatePromptVersion,
            work.Input,
            work.ProductionOutput,
            candidateOutput,
            lifecycle,
            scores,
            latency,
            attempt,
            errorCode,
            errorMessage,
            work.PublishedAt,
            completedAt);
}
