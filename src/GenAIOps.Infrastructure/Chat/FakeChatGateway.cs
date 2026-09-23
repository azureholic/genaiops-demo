using GenAIOps.Application.Chat;

namespace GenAIOps.Infrastructure.Chat;

public sealed class FakeChatGateway : IChatGateway
{
    public Task<ChatGatewayResponse> SendAsync(
        ChatGatewayRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string responseId = $"local-{StableHash(request.CorrelationId):x8}";
        ChatGatewayResponse response = new(
            $"Local agent {request.AgentName}/{request.AgentVersion}: {request.Message}",
            responseId,
            [
                new ChatCitation(
                    "uri",
                    "Local development source",
                    "https://localhost/docs/fake",
                    null,
                    null,
                    0,
                    request.Message.Length),
            ],
            [new ChatToolCall($"{responseId}-tool", "function", "local_lookup", "completed")],
            new ChatUsage(
                InputTokens: Math.Max(1, request.Message.Length / 4),
                OutputTokens: Math.Max(1, request.Message.Length / 5),
                TotalTokens: Math.Max(2, request.Message.Length / 4 + request.Message.Length / 5)));
        return Task.FromResult(response);
    }

    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return hash;
    }
}
