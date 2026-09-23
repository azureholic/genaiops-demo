using GenAIOps.Application.Chat;

namespace GenAIOps.Application.Shadow;

public sealed record ShadowWorkItem(
    string CorrelationId,
    string RegistryId,
    string ProductionAgentId,
    string ProductionPromptVersion,
    string CandidateAgentId,
    string CandidatePromptVersion,
    string Input,
    string ProductionOutput,
    DateTimeOffset PublishedAt,
    string? ExperimentId = null,
    string? AssignedPromptVersion = null,
    string? TraceParent = null,
    string? TraceState = null);

public sealed record ShadowWorkDelivery(
    ShadowWorkItem Work,
    int Attempt,
    string? Receipt = null);

public interface IShadowWorkPublisher
{
    bool TryPublish(ShadowWorkItem work);
}

public interface IShadowWorkQueue : IShadowWorkPublisher
{
    ValueTask<ShadowWorkDelivery> ReceiveAsync(CancellationToken cancellationToken = default);

    void Complete(ShadowWorkDelivery delivery);

    void Retry(ShadowWorkDelivery delivery);

    void DeadLetter(ShadowWorkDelivery delivery, string reason);
}

public sealed record EvaluationInput(
    string Input,
    string ProductionOutput,
    ChatGatewayResponse CandidateResponse);

public sealed record EvaluationScores(
    double TaskAdherence,
    double Groundedness,
    double ToolAccuracy)
{
    public IReadOnlyDictionary<string, double> ToDictionary() =>
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["taskAdherence"] = TaskAdherence,
            ["groundedness"] = Groundedness,
            ["toolAccuracy"] = ToolAccuracy,
        };
}

public interface IResponseEvaluator
{
    Task<EvaluationScores> EvaluateAsync(
        EvaluationInput input,
        CancellationToken cancellationToken = default);
}

public sealed class EvaluationProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public enum ShadowProcessingDisposition
{
    Completed,
    Duplicate,
    Retry,
    Poisoned,
}

public sealed record ShadowProcessingResult(
    ShadowProcessingDisposition Disposition,
    string? ErrorCode = null);

public interface IShadowEvaluationProcessor
{
    Task<ShadowProcessingResult> ProcessAsync(
        ShadowWorkDelivery delivery,
        CancellationToken cancellationToken = default);
}
