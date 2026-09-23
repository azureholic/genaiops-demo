namespace GenAIOps.Application.Chat;

public sealed record ChatGatewayRequest(
    string AgentName,
    string AgentVersion,
    string Message,
    string CorrelationId);

public sealed record ChatCitation(
    string Kind,
    string? Title,
    string? Uri,
    string? FileId,
    string? FileName,
    int? StartIndex,
    int? EndIndex);

public sealed record ChatToolCall(
    string Id,
    string Kind,
    string Name,
    string Status);

public sealed record ChatUsage(int InputTokens, int OutputTokens, int TotalTokens);

public sealed record ChatGatewayResponse(
    string Message,
    string ProviderResponseId,
    IReadOnlyList<ChatCitation> Citations,
    IReadOnlyList<ChatToolCall> ToolCalls,
    ChatUsage? Usage);

public sealed record ChatResponse(
    string Message,
    string CorrelationId,
    string ProviderResponseId,
    IReadOnlyList<ChatCitation> Citations,
    IReadOnlyList<ChatToolCall> ToolCalls,
    ChatUsage? Usage,
    string AssignedPromptVersion,
    string? ExperimentId);

public interface IChatGateway
{
    Task<ChatGatewayResponse> SendAsync(
        ChatGatewayRequest request,
        CancellationToken cancellationToken = default);
}

public interface IChatService
{
    Task<ChatResponse> SendAsync(
        string registryId,
        string message,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? assignmentKey = null);
}

public abstract class ChatException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed class ChatValidationException(string message)
    : ChatException("invalid_request", message);

public sealed class ProductionAgentNotFoundException(string registryId)
    : ChatException(
        "production_agent_not_found",
        $"Agent registry '{registryId}' has no production assignment.");

public sealed class ChatProviderException(string message, Exception? innerException = null)
    : ChatException("provider_failure", message, innerException);

public sealed class ChatRequestCanceledException(Exception? innerException = null)
    : ChatException("request_cancelled", "The chat request was cancelled.", innerException);

public sealed class ChatMetadataPersistenceException(Exception innerException)
    : ChatException(
        "metadata_persistence_failure",
        "Chat request metadata could not be persisted.",
        innerException);
