using System.Text.Json;
using GenAIOps.Application.Chat;
using GenAIOps.Application.Metrics;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Shadow;
using GenAIOps.Domain.Records;

namespace GenAIOps.Infrastructure.Metrics;

public sealed class FakeContinuousEvaluationProvider : IContinuousEvaluationProvider
{
    private static readonly IReadOnlyDictionary<string, EvaluationScores> Baselines =
        new Dictionary<string, EvaluationScores>(StringComparer.Ordinal)
        {
            ["v1"] = new(0.82, 0.89, 0.84),
            ["v2"] = new(0.94, 0.97, 0.95),
            ["v3"] = new(0.72, 0.75, 0.58),
        };

    public Task<ContinuousEvaluationResult> EvaluateAsync(
        ContinuousEvaluationFixture fixture,
        string promptVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EvaluationScores scores = Baselines.TryGetValue(promptVersion, out EvaluationScores? value)
            ? value
            : throw new ArgumentException(
                $"No deterministic baseline exists for '{promptVersion}'.",
                nameof(promptVersion));
        string output = string.Join(' ', fixture.ExpectedResponseTerms)
            + $" [{fixture.RequiredTool}]";
        long latency = 20 + StableHash($"{fixture.Id}:{promptVersion}") % 80;
        return Task.FromResult(
            new ContinuousEvaluationResult(
                output,
                scores.TaskAdherence,
                scores.Groundedness,
                scores.ToolAccuracy,
                latency));
    }

    private static int StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return (int)(hash & int.MaxValue);
    }
}

public sealed class GatewayContinuousEvaluationProvider(
    IChatGateway gateway,
    IResponseEvaluator evaluator) : IContinuousEvaluationProvider
{
    public async Task<ContinuousEvaluationResult> EvaluateAsync(
        ContinuousEvaluationFixture fixture,
        string promptVersion,
        CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ChatGatewayResponse response = await gateway.SendAsync(
            new ChatGatewayRequest(
                $"continuous-{promptVersion}",
                promptVersion,
                fixture.UserMessage,
                $"continuous-{fixture.Id}-{promptVersion}"),
            cancellationToken);
        stopwatch.Stop();
        EvaluationScores scores = await evaluator.EvaluateAsync(
            new EvaluationInput(
                fixture.UserMessage,
                string.Join(' ', fixture.ExpectedResponseTerms),
                response),
            cancellationToken);
        return new ContinuousEvaluationResult(
            response.Message,
            scores.TaskAdherence,
            scores.Groundedness,
            scores.ToolAccuracy,
            stopwatch.ElapsedMilliseconds);
    }
}

public sealed class ContinuousEvaluationRunner(
    IContinuousEvaluationProvider provider,
    IRepository<ShadowEvaluationRecord> evaluations,
    ContinuousEvaluationOptions options) : IContinuousEvaluationRunner
{
    public async Task<int> RunAsync(
        string registryId,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        if (scheduledAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Scheduled time must be UTC.", nameof(scheduledAt));
        }

        IReadOnlyList<ContinuousEvaluationFixture> fixtures = LoadFixtures(options.DatasetPath);
        int created = 0;
        foreach (ContinuousEvaluationFixture fixture in fixtures)
        {
            foreach (string promptVersion in fixture.PromptVersions)
            {
                string id =
                    $"continuous-{scheduledAt:yyyyMMddHHmmss}-{fixture.Id}-{promptVersion}";
                if (await evaluations.GetAsync(id, registryId, cancellationToken) is not null)
                {
                    continue;
                }

                ContinuousEvaluationResult result =
                    await provider.EvaluateAsync(fixture, promptVersion, cancellationToken);
                ShadowEvaluationRecord record = new(
                    id,
                    registryId,
                    id,
                    registryId,
                    "continuous-baseline",
                    promptVersion,
                    $"continuous-{promptVersion}",
                    promptVersion,
                    fixture.UserMessage,
                    result.Output,
                    result.Output,
                    EvaluationLifecycle.Completed,
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["taskAdherence"] = result.TaskAdherence,
                        ["groundedness"] = result.Groundedness,
                        ["toolAccuracy"] = result.ToolAccuracy,
                    },
                    result.LatencyMilliseconds,
                    AttemptCount: 1,
                    ErrorCode: null,
                    ErrorMessage: null,
                    CreatedAt: scheduledAt,
                    CompletedAt: scheduledAt,
                    AssignedPromptVersion: promptVersion);
                try
                {
                    await evaluations.CreateAsync(record, cancellationToken);
                    created++;
                }
                catch (RecordConflictException)
                {
                    // Another scheduler completed the stable fixture/version identity.
                }
            }
        }

        return created;
    }

    public static IReadOnlyList<ContinuousEvaluationFixture> LoadFixtures(string datasetPath)
    {
        if (!Directory.Exists(datasetPath))
        {
            throw new DirectoryNotFoundException(
                $"Evaluation dataset directory '{datasetPath}' was not found.");
        }

        return Directory.EnumerateFiles(datasetPath, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(
                path =>
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                    JsonElement root = document.RootElement;
                    JsonElement expected = root.GetProperty("expected");
                    return new ContinuousEvaluationFixture(
                        root.GetProperty("id").GetString()
                            ?? throw new JsonException("Fixture id is required."),
                        root.GetProperty("userMessage").GetString()
                            ?? throw new JsonException("Fixture userMessage is required."),
                        root.GetProperty("promptVersions").EnumerateArray()
                            .Select(value => value.GetString()!)
                            .ToArray(),
                        expected.GetProperty("responseContains").EnumerateArray()
                            .Select(value => value.GetString()!)
                            .ToArray(),
                        expected.GetProperty("requiredTool").GetString()
                            ?? throw new JsonException("Fixture requiredTool is required."));
                })
            .ToArray();
    }
}

public sealed record ContinuousEvaluationOptions(
    string DatasetPath,
    string RegistryId,
    TimeSpan Interval,
    TimeSpan AggregationWindow)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DatasetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(RegistryId);
        if (Interval <= TimeSpan.Zero || AggregationWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Interval),
                "Schedule intervals must be positive.");
        }
    }
}
