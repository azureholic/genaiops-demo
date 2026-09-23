using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenAIOps.Domain.Prompts;

public static class PromptArtifactValidator
{
    private const string PromptSchemaReference = "../../Schemas/prompt-metadata.schema.json";
    private const string FixtureSchemaReference = "../Schemas/evaluation-fixture.schema.json";

    private static readonly IReadOnlyDictionary<string, ExpectedMetrics> SpecificationMetrics =
        new Dictionary<string, ExpectedMetrics>(StringComparer.Ordinal)
        {
            ["v1"] = new(82, 89, 84),
            ["v2"] = new(94, 97, 95),
            ["v3"] = new(72, 75, 58),
        };

    private static readonly string[] PoorV3Behaviors =
    [
        "Use internal reasoning first",
        "Avoid tools whenever possible",
        "Do not ask clarifying questions",
        "If uncertain, provide your best guess",
    ];

    public static PromptValidationResult Validate(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        List<string> errors = [];
        ValidateSchema(Path.Combine(repositoryRoot, "Schemas", "prompt-metadata.schema.json"), errors);
        ValidateSchema(Path.Combine(repositoryRoot, "Schemas", "evaluation-fixture.schema.json"), errors);

        HashSet<string> versions = ValidatePrompts(
            Path.Combine(repositoryRoot, "Prompts"),
            errors);
        ValidateFixtures(
            Path.Combine(repositoryRoot, "EvaluationData"),
            versions,
            errors);

        errors.Sort(StringComparer.Ordinal);
        return new PromptValidationResult(errors);
    }

    private static void ValidateSchema(string path, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{RelativeName(path)}: schema file is missing.");
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{RelativeName(path)}: schema root must be an object.");
            }
        }
        catch (JsonException exception)
        {
            errors.Add($"{RelativeName(path)}: invalid JSON ({exception.Message}).");
        }
    }

    private static HashSet<string> ValidatePrompts(string promptsPath, List<string> errors)
    {
        HashSet<string> versions = new(StringComparer.Ordinal);
        if (!Directory.Exists(promptsPath))
        {
            errors.Add("Prompts: directory is missing.");
            return versions;
        }

        string[] directories = Directory.GetDirectories(promptsPath);
        Array.Sort(directories, StringComparer.Ordinal);
        foreach (string directory in directories)
        {
            string directoryVersion = Path.GetFileName(directory);
            string promptPath = Path.Combine(directory, "prompt.md");
            string? prompt = File.Exists(promptPath) ? File.ReadAllText(promptPath) : null;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                errors.Add($"Prompts/{directoryVersion}/prompt.md must not be empty.");
            }
            else if (string.Equals(directoryVersion, "v3", StringComparison.Ordinal))
            {
                foreach (string behavior in PoorV3Behaviors)
                {
                    if (!prompt.Contains(behavior, StringComparison.Ordinal))
                    {
                        errors.Add($"Prompts/v3/prompt.md: missing required poor behavior '{behavior}'.");
                    }
                }
            }

            PromptMetadata? metadata = ReadJson<PromptMetadata>(
                Path.Combine(directory, "metadata.json"),
                $"Prompts/{directoryVersion}/metadata.json",
                errors);
            if (metadata is null)
            {
                continue;
            }

            ValidateRequiredProperties(
                Path.Combine(directory, "metadata.json"),
                $"Prompts/{directoryVersion}/metadata.json",
                ["$schema", "version", "description", "expectedMetrics", "intentionallyPoor"],
                errors);
            ValidateRequiredProperties(
                Path.Combine(directory, "metadata.json"),
                $"Prompts/{directoryVersion}/metadata.json expectedMetrics",
                ["taskAdherence", "groundedness", "toolAccuracy"],
                errors,
                "expectedMetrics");
            ValidatePromptMetadata(metadata, directoryVersion, errors);
            if (!string.IsNullOrWhiteSpace(metadata.Version) && !versions.Add(metadata.Version))
            {
                errors.Add($"Prompts: duplicate version '{metadata.Version}'.");
            }
        }

        foreach (string requiredVersion in SpecificationMetrics.Keys)
        {
            if (!versions.Contains(requiredVersion))
            {
                errors.Add($"Prompts: required version '{requiredVersion}' is missing.");
            }
        }

        if (versions.Count != SpecificationMetrics.Count)
        {
            errors.Add("Prompts: exactly v1, v2, and v3 must be present.");
        }

        return versions;
    }

    private static void ValidatePromptMetadata(
        PromptMetadata metadata,
        string directoryVersion,
        List<string> errors)
    {
        string location = $"Prompts/{directoryVersion}/metadata.json";
        RequireEqual(metadata.Schema, PromptSchemaReference, location, "$schema", errors);
        RequireNotEmpty(metadata.Version, location, "version", errors);
        RequireNotEmpty(metadata.Description, location, "description", errors);

        if (!string.Equals(metadata.Version, directoryVersion, StringComparison.Ordinal))
        {
            errors.Add($"{location}: version must match directory name '{directoryVersion}'.");
        }

        if (metadata.ExpectedMetrics is null)
        {
            errors.Add($"{location}: expectedMetrics is required.");
        }
        else
        {
            ValidatePercentage(metadata.ExpectedMetrics.TaskAdherence, location, "taskAdherence", errors);
            ValidatePercentage(metadata.ExpectedMetrics.Groundedness, location, "groundedness", errors);
            ValidatePercentage(metadata.ExpectedMetrics.ToolAccuracy, location, "toolAccuracy", errors);

            if (metadata.Version is not null
                && SpecificationMetrics.TryGetValue(metadata.Version, out ExpectedMetrics? expected)
                && metadata.ExpectedMetrics != expected)
            {
                errors.Add($"{location}: expected metrics do not match the specification.");
            }
        }

        bool shouldBePoor = string.Equals(metadata.Version, "v3", StringComparison.Ordinal);
        if (metadata.IntentionallyPoor != shouldBePoor)
        {
            errors.Add($"{location}: intentionallyPoor must be {shouldBePoor.ToString().ToLowerInvariant()}.");
        }

    }

    private static void ValidateFixtures(
        string evaluationDataPath,
        IReadOnlySet<string> versions,
        List<string> errors)
    {
        if (!Directory.Exists(evaluationDataPath))
        {
            errors.Add("EvaluationData: directory is missing.");
            return;
        }

        string[] files = Directory.GetFiles(evaluationDataPath, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length == 0)
        {
            errors.Add("EvaluationData: at least one fixture is required.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string location = $"EvaluationData/{Path.GetFileName(file)}";
            EvaluationFixture? fixture = ReadJson<EvaluationFixture>(file, location, errors);
            if (fixture is null)
            {
                continue;
            }

            ValidateRequiredProperties(
                file,
                location,
                ["$schema", "id", "category", "userMessage", "promptVersions", "expected"],
                errors);
            ValidateRequiredProperties(
                file,
                $"{location} expected",
                ["responseContains", "requiredTool", "shouldAskClarifyingQuestion"],
                errors,
                "expected");
            RequireEqual(fixture.Schema, FixtureSchemaReference, location, "$schema", errors);
            RequireNotEmpty(fixture.Id, location, "id", errors);
            RequireNotEmpty(fixture.Category, location, "category", errors);
            RequireNotEmpty(fixture.UserMessage, location, "userMessage", errors);
            if (!string.IsNullOrWhiteSpace(fixture.Id)
                && !Regex.IsMatch(fixture.Id, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            {
                errors.Add($"{location}: id must use lowercase kebab-case.");
            }

            if (!string.IsNullOrWhiteSpace(fixture.Id) && !ids.Add(fixture.Id))
            {
                errors.Add($"{location}: duplicate fixture id '{fixture.Id}'.");
            }

            if (fixture.PromptVersions is null || fixture.PromptVersions.Count == 0)
            {
                errors.Add($"{location}: promptVersions must contain at least one version.");
            }
            else
            {
                foreach (string version in fixture.PromptVersions)
                {
                    if (!versions.Contains(version))
                    {
                        errors.Add($"{location}: promptVersions references unknown version '{version}'.");
                    }
                }

                if (fixture.PromptVersions.Count != fixture.PromptVersions.Distinct(StringComparer.Ordinal).Count())
                {
                    errors.Add($"{location}: promptVersions must contain unique values.");
                }
            }

            if (fixture.Expected is null)
            {
                errors.Add($"{location}: expected is required.");
            }
            else if (fixture.Expected.ResponseContains is null
                     || fixture.Expected.ResponseContains.Count == 0
                     || fixture.Expected.ResponseContains.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"{location}: expected.responseContains must contain non-empty values.");
            }
            else if (fixture.Expected.ResponseContains.Count
                     != fixture.Expected.ResponseContains.Distinct(StringComparer.Ordinal).Count())
            {
                errors.Add($"{location}: expected.responseContains must contain unique values.");
            }

            if (fixture.Expected is not null
                && fixture.Expected.RequiredTool is not null
                && string.IsNullOrWhiteSpace(fixture.Expected.RequiredTool))
            {
                errors.Add($"{location}: expected.requiredTool must be null or a non-empty string.");
            }
        }
    }

    private static void ValidateRequiredProperties(
        string path,
        string location,
        IReadOnlyList<string> requiredProperties,
        List<string> errors,
        string? objectProperty = null)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement element = document.RootElement;
        if (objectProperty is not null
            && (!element.TryGetProperty(objectProperty, out element)
                || element.ValueKind != JsonValueKind.Object))
        {
            return;
        }

        foreach (string property in requiredProperties)
        {
            if (!element.TryGetProperty(property, out _))
            {
                errors.Add($"{location}: {property} is required.");
            }
        }
    }

    private static T? ReadJson<T>(string path, string location, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{location}: file is missing.");
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
                });
        }
        catch (JsonException exception)
        {
            errors.Add($"{location}: invalid JSON ({exception.Message}).");
            return default;
        }
    }

    private static void ValidatePercentage(int value, string location, string name, List<string> errors)
    {
        if (value is < 0 or > 100)
        {
            errors.Add($"{location}: expectedMetrics.{name} must be between 0 and 100.");
        }
    }

    private static void RequireNotEmpty(
        string? value,
        string location,
        string name,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{location}: {name} is required.");
        }
    }

    private static void RequireEqual(
        string? actual,
        string expected,
        string location,
        string name,
        List<string> errors)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            errors.Add($"{location}: {name} must equal '{expected}'.");
        }
    }

    private static string RelativeName(string path) => Path.GetFileName(path);

    private sealed record PromptMetadata(
        [property: System.Text.Json.Serialization.JsonPropertyName("$schema")] string? Schema,
        string? Version,
        string? Description,
        ExpectedMetrics? ExpectedMetrics,
        bool IntentionallyPoor);

    private sealed record ExpectedMetrics(int TaskAdherence, int Groundedness, int ToolAccuracy);

    private sealed record EvaluationFixture(
        [property: System.Text.Json.Serialization.JsonPropertyName("$schema")] string? Schema,
        string? Id,
        string? Category,
        string? UserMessage,
        IReadOnlyList<string>? PromptVersions,
        FixtureExpectation? Expected);

    private sealed record FixtureExpectation(
        IReadOnlyList<string>? ResponseContains,
        string? RequiredTool,
        bool ShouldAskClarifyingQuestion);
}
