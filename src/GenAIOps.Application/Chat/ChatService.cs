using GenAIOps.Application.Experiments;
using GenAIOps.Application.Observability;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Application.Shadow;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;

namespace GenAIOps.Application.Chat;

public sealed class ChatService(
    IAgentRegistryService registry,
    IChatGateway gateway,
    IRepository<ChatRequestMetadataRecord> metadataRepository,
    IShadowWorkPublisher? shadowPublisher = null,
    IExperimentRouter? experimentRouter = null) : IChatService
{
    public const int MaximumMessageLength = 8_000;

    public async Task<ChatResponse> SendAsync(
        string registryId,
        string message,
        string correlationId,
        CancellationToken cancellationToken = default,
        string? assignmentKey = null)
    {
        using GenAIOpsTelemetry.TelemetryOperation operation =
            GenAIOpsTelemetry.StartOperation("chat.send", "chat");
        Validate(registryId, message, correlationId);

        RegistrySnapshot? snapshot;
        try
        {
            snapshot = await registry.GetAsync(registryId, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            throw new ChatRequestCanceledException(exception);
        }

        AgentAssignment production = snapshot?.State.Production
            ?? throw new ProductionAgentNotFoundException(registryId);
        ExperimentAssignment assignment;
        try
        {
            assignment = experimentRouter is null
                ? new ExperimentAssignment(
                    ExperimentId: null,
                    production.AgentId,
                    production.PromptVersion,
                    DateTimeOffset.UtcNow)
                : await experimentRouter.AssignAsync(
                    registryId,
                    snapshot!,
                    assignmentKey,
                    cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            throw new ChatRequestCanceledException(exception);
        }
        AgentAssignment routed = new(
            assignment.AgentId,
            assignment.PromptVersion,
            AssignmentSlot.Production,
            assignment.AssignedAt);
        operation.SetPromptVersion(routed.PromptVersion);

        ChatGatewayRequest gatewayRequest = new(
            routed.AgentId,
            routed.PromptVersion,
            message,
            correlationId);

        try
        {
            ChatGatewayResponse result;
            using (GenAIOpsTelemetry.TelemetryOperation invocation =
                GenAIOpsTelemetry.StartOperation(
                    "chat.provider.invoke",
                    "chat.provider",
                    routed.PromptVersion))
            {
                result = await gateway.SendAsync(gatewayRequest, cancellationToken);
                invocation.Complete("success");
            }
            await PersistMetadataAsync(
                registryId,
                routed,
                assignment,
                correlationId,
                message.Length,
                "succeeded",
                result.ProviderResponseId);
            if (snapshot?.State.Candidate is { } candidate)
            {
                TryPublishShadow(
                    CreateShadowWorkItem(
                        correlationId,
                        registryId,
                        routed.AgentId,
                        routed.PromptVersion,
                        candidate.AgentId,
                        candidate.PromptVersion,
                        message,
                        result.Message,
                        DateTimeOffset.UtcNow,
                        assignment.ExperimentId,
                        assignment.PromptVersion));
            }

            operation.Complete("success");
            return new ChatResponse(
                result.Message,
                correlationId,
                result.ProviderResponseId,
                result.Citations,
                result.ToolCalls,
                result.Usage,
                assignment.PromptVersion,
                assignment.ExperimentId);
        }
        catch (OperationCanceledException exception)
        {
            operation.Complete("cancelled");
            await PersistMetadataAsync(
                registryId,
                routed,
                assignment,
                correlationId,
                message.Length,
                "cancelled",
                null);
            throw new ChatRequestCanceledException(exception);
        }
        catch (ChatProviderException)
        {
            operation.Complete("failure");
            await PersistMetadataAsync(
                registryId,
                routed,
                assignment,
                correlationId,
                message.Length,
                "failed",
                null);
            throw;
        }
        catch (ChatMetadataPersistenceException)
        {
            operation.Complete("failure");
            throw;
        }
        catch (Exception exception)
        {
            operation.Complete("failure");
            await PersistMetadataAsync(
                registryId,
                routed,
                assignment,
                correlationId,
                message.Length,
                "failed",
                null);
            throw new ChatProviderException("The Foundry agent request failed.", exception);
        }
    }

    private static ShadowWorkItem CreateShadowWorkItem(
            string correlationId,
            string registryId,
            string productionAgentId,
            string productionPromptVersion,
            string candidateAgentId,
            string candidatePromptVersion,
            string input,
            string productionOutput,
            DateTimeOffset publishedAt,
            string? experimentId,
            string? assignedPromptVersion)
    {
        (string? traceParent, string? traceState) =
            GenAIOpsTelemetry.CapturePropagationContext();
        return new ShadowWorkItem(
            correlationId,
            registryId,
            productionAgentId,
            productionPromptVersion,
            candidateAgentId,
            candidatePromptVersion,
            input,
            productionOutput,
            publishedAt,
            experimentId,
            assignedPromptVersion,
            traceParent,
            traceState);
    }

    private static void Validate(string registryId, string message, string correlationId)
    {
        if (string.IsNullOrWhiteSpace(registryId))
        {
            throw new ChatValidationException("Registry ID is required.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ChatValidationException("Message is required.");
        }

        if (message.Length > MaximumMessageLength)
        {
            throw new ChatValidationException(
                $"Message must not exceed {MaximumMessageLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
        {
            throw new ChatValidationException(
                "Correlation ID is required and must not exceed 128 characters.");
        }
    }

    private async Task PersistMetadataAsync(
        string registryId,
        AgentAssignment production,
        ExperimentAssignment assignment,
        string correlationId,
        int inputCharacterCount,
        string outcome,
        string? providerResponseId)
    {
        try
        {
            await metadataRepository.CreateAsync(
                new ChatRequestMetadataRecord(
                    Guid.NewGuid().ToString("N"),
                    registryId,
                    correlationId,
                    registryId,
                    production.AgentId,
                    production.PromptVersion,
                    DateTimeOffset.UtcNow,
                    inputCharacterCount,
                    outcome,
                    providerResponseId,
                    assignment.ExperimentId,
                    assignment.PromptVersion),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            throw new ChatMetadataPersistenceException(exception);
        }
    }

    private void TryPublishShadow(ShadowWorkItem work)
    {
        try
        {
            shadowPublisher?.TryPublish(work);
        }
        catch
        {
            // Shadow publication is best-effort on the visible request path. Production response
            // success must never be changed by candidate pipeline availability.
        }
    }
}
