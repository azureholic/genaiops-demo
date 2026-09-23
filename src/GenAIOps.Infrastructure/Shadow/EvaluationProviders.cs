using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using GenAIOps.Application.Shadow;

namespace GenAIOps.Infrastructure.Shadow;

public sealed class FakeResponseEvaluator : IResponseEvaluator
{
    public Task<EvaluationScores> EvaluateAsync(
        EvaluationInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        double overlap = WordOverlap(input.ProductionOutput, input.CandidateResponse.Message);
        double groundedness = input.CandidateResponse.Citations.Count > 0 ? 1 : 0.5;
        double toolAccuracy = input.CandidateResponse.ToolCalls.All(
            call => string.Equals(call.Status, "completed", StringComparison.OrdinalIgnoreCase))
            ? 1
            : 0;
        return Task.FromResult(
            new EvaluationScores(
                TaskAdherence: Math.Round(overlap, 4),
                Groundedness: groundedness,
                ToolAccuracy: toolAccuracy));
    }

    private static double WordOverlap(string left, string right)
    {
        HashSet<string> expected = Words(left);
        if (expected.Count == 0)
        {
            return 1;
        }

        HashSet<string> actual = Words(right);
        return (double)expected.Count(actual.Contains) / expected.Count;
    }

    private static HashSet<string> Words(string value) =>
        value.Split(
                [' ', '\t', '\r', '\n', '.', ',', ':', ';', '!', '?'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
}

public interface IFoundryEvaluationClient
{
    Task<EvaluationScores> EvaluateAsync(
        EvaluationInput input,
        CancellationToken cancellationToken = default);
}

public sealed class FoundryResponseEvaluator(IFoundryEvaluationClient client) : IResponseEvaluator
{
    public Task<EvaluationScores> EvaluateAsync(
        EvaluationInput input,
        CancellationToken cancellationToken = default) =>
        client.EvaluateAsync(input, cancellationToken);
}

public sealed record FoundryEvaluationOptions(
    Uri Endpoint,
    string TokenScope = "https://ai.azure.com/.default",
    string? ManagedIdentityClientId = null)
{
    public void Validate()
    {
        if (!Endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Foundry evaluation endpoint must be absolute.", nameof(Endpoint));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(TokenScope);
    }
}

public sealed class FoundryEvaluationClient : IFoundryEvaluationClient
{
    private readonly HttpClient httpClient;
    private readonly TokenCredential credential;
    private readonly FoundryEvaluationOptions options;

    public FoundryEvaluationClient(
        HttpClient httpClient,
        FoundryEvaluationOptions options)
        : this(httpClient, options, CreateCredential(options))
    {
    }

    public FoundryEvaluationClient(
        HttpClient httpClient,
        FoundryEvaluationOptions options,
        TokenCredential credential)
    {
        options.Validate();
        this.httpClient = httpClient;
        this.options = options;
        this.credential = credential;
    }

    private static TokenCredential CreateCredential(FoundryEvaluationOptions options)
    {
        DefaultAzureCredentialOptions credentialOptions = new();
        if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
        {
            credentialOptions.ManagedIdentityClientId = options.ManagedIdentityClientId;
        }

        return new DefaultAzureCredential(credentialOptions);
    }

    public async Task<EvaluationScores> EvaluateAsync(
        EvaluationInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            AccessToken token = await credential.GetTokenAsync(
                new TokenRequestContext([options.TokenScope]),
                cancellationToken);
            using HttpRequestMessage request = new(HttpMethod.Post, options.Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Content = JsonContent.Create(
                new
                {
                    evaluators = new[]
                    {
                        new { name = "taskAdherence", evaluatorName = "builtin.task_adherence" },
                        new { name = "groundedness", evaluatorName = "builtin.groundedness" },
                        new { name = "toolAccuracy", evaluatorName = "builtin.tool_call_accuracy" },
                    },
                    data = new
                    {
                        input = input.Input,
                        productionOutput = input.ProductionOutput,
                        candidateOutput = input.CandidateResponse.Message,
                        citations = input.CandidateResponse.Citations,
                        toolCalls = input.CandidateResponse.ToolCalls,
                    },
                });
            using HttpResponseMessage response =
                await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using JsonDocument body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            JsonElement scores = body.RootElement.GetProperty("scores");
            return new EvaluationScores(
                ReadNormalizedScore(scores, "taskAdherence"),
                ReadNormalizedScore(scores, "groundedness"),
                ReadNormalizedScore(scores, "toolAccuracy"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EvaluationProviderException(
                "The Foundry evaluation request failed.",
                exception);
        }
    }

    private static double ReadNormalizedScore(JsonElement scores, string name)
    {
        double score = scores.GetProperty(name).GetDouble();
        if (score is < 0 or > 1)
        {
            throw new JsonException($"Evaluation score '{name}' must be between 0 and 1.");
        }

        return score;
    }
}
