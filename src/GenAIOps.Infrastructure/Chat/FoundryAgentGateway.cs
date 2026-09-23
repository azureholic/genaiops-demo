using System.Text;
using Azure;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using GenAIOps.Application.Chat;

namespace GenAIOps.Infrastructure.Chat;

public sealed record FoundryAgentOptions(
    Uri ProjectEndpoint,
    string? ManagedIdentityClientId = null);

public sealed class FoundryAgentGateway : IChatGateway
{
    private readonly AIProjectClient projectClient;

    public FoundryAgentGateway(FoundryAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        DefaultAzureCredentialOptions credentialOptions = new();
        if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
        {
            credentialOptions.ManagedIdentityClientId = options.ManagedIdentityClientId;
        }

        projectClient = new AIProjectClient(
            options.ProjectEndpoint,
            new DefaultAzureCredential(credentialOptions));
    }

    public async Task<ChatGatewayResponse> SendAsync(
        ChatGatewayRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ProjectsAgentVersion version =
                await projectClient.AgentAdministrationClient.GetAgentVersionAsync(
                    request.AgentName,
                    request.AgentVersion,
                    cancellationToken);
            ProjectConversation conversation =
                await projectClient.ProjectOpenAIClient
                    .GetProjectConversationsClient()
                    .CreateProjectConversationAsync(cancellationToken: cancellationToken);
            ProjectResponsesClient responsesClient =
                projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(
                    new AgentReference(version.Name, version.Version),
                    conversation.Id);
            // The GA Azure client currently exposes response models marked experimental by the
            // upstream OpenAI package. Keep that unstable model surface behind this dynamic mapper
            // instead of suppressing OPENAI001 or leaking those types into application contracts.
            dynamic response =
                await responsesClient.CreateResponseAsync(
                    request.Message,
                    cancellationToken: cancellationToken);

            return Map(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestFailedException exception)
        {
            throw new ChatProviderException("The Foundry agent request failed.", exception);
        }
    }

    private static ChatGatewayResponse Map(dynamic response)
    {
        List<ChatCitation> citations = [];
        List<ChatToolCall> toolCalls = [];
        StringBuilder text = new();

        foreach (dynamic item in response.OutputItems)
        {
            string itemType = item.GetType().Name;
            if (string.Equals(itemType, "MessageResponseItem", StringComparison.Ordinal))
            {
                AddMessage(item, text, citations);
            }
            else if (string.Equals(itemType, "FunctionCallResponseItem", StringComparison.Ordinal))
            {
                toolCalls.Add(
                    new ChatToolCall(
                        (string)item.CallId,
                        "function",
                        (string)item.FunctionName,
                        item.Status?.ToString() ?? "unknown"));
            }
            else if (string.Equals(itemType, "FileSearchCallResponseItem", StringComparison.Ordinal))
            {
                toolCalls.Add(
                    new ChatToolCall(
                        (string)item.Id,
                        "file_search",
                        "file_search",
                        item.Status?.ToString() ?? "unknown"));
            }
            else if (string.Equals(itemType, "WebSearchCallResponseItem", StringComparison.Ordinal))
            {
                toolCalls.Add(
                    new ChatToolCall(
                        (string)item.Id,
                        "web_search",
                        "web_search",
                        item.Status?.ToString() ?? "unknown"));
            }
        }

        dynamic? tokenUsage = response.Usage;
        ChatUsage? usage = tokenUsage is null
            ? null
            : new ChatUsage(
                (int)tokenUsage.InputTokenCount,
                (int)tokenUsage.OutputTokenCount,
                (int)tokenUsage.TotalTokenCount);
        return new ChatGatewayResponse(
            text.ToString(),
            (string)response.Id,
            citations,
            toolCalls,
            usage);
    }

    private static void AddMessage(
        dynamic message,
        StringBuilder text,
        ICollection<ChatCitation> citations)
    {
        foreach (dynamic part in message.Content)
        {
            string? partText = part.Text;
            if (!string.IsNullOrEmpty(partText))
            {
                text.Append(partText);
            }

            foreach (dynamic annotation in part.OutputTextAnnotations)
            {
                string annotationType = annotation.GetType().Name;
                if (string.Equals(
                    annotationType,
                    "UriCitationMessageAnnotation",
                    StringComparison.Ordinal))
                {
                    citations.Add(
                        new ChatCitation(
                            "uri",
                            (string?)annotation.Title,
                            annotation.Uri?.ToString(),
                            null,
                            null,
                            (int?)annotation.StartIndex,
                            (int?)annotation.EndIndex));
                }
                else if (string.Equals(
                    annotationType,
                    "FileCitationMessageAnnotation",
                    StringComparison.Ordinal))
                {
                    citations.Add(
                        new ChatCitation(
                            "file",
                            (string?)annotation.Filename,
                            null,
                            (string?)annotation.FileId,
                            (string?)annotation.Filename,
                            (int?)annotation.Index,
                            null));
                }
                else if (string.Equals(
                    annotationType,
                    "FilePathMessageAnnotation",
                    StringComparison.Ordinal))
                {
                    citations.Add(
                        new ChatCitation(
                            "file_path",
                            null,
                            null,
                            (string?)annotation.FileId,
                            null,
                            (int?)annotation.Index,
                            null));
                }
            }
        }
    }
}
