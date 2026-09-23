using System.Text.Json;
using GenAIOps.Domain.Prompts;

namespace GenAIOps.UnitTests;

public sealed class PromptArtifactValidatorTests
{
    [Fact]
    public void Repository_prompt_artifacts_are_valid()
    {
        PromptValidationResult result = PromptArtifactValidator.Validate(FindRepositoryRoot());

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Theory]
    [InlineData("v1", 82, 89, 84)]
    [InlineData("v2", 94, 97, 95)]
    [InlineData("v3", 72, 75, 58)]
    public void Metadata_contains_specified_metrics(
        string version,
        int taskAdherence,
        int groundedness,
        int toolAccuracy)
    {
        string path = Path.Combine(FindRepositoryRoot(), "Prompts", version, "metadata.json");
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement metrics = metadata.RootElement.GetProperty("expectedMetrics");

        Assert.Equal(version, metadata.RootElement.GetProperty("version").GetString());
        Assert.Equal(taskAdherence, metrics.GetProperty("taskAdherence").GetInt32());
        Assert.Equal(groundedness, metrics.GetProperty("groundedness").GetInt32());
        Assert.Equal(toolAccuracy, metrics.GetProperty("toolAccuracy").GetInt32());
    }

    [Fact]
    public void V3_contains_every_intentionally_poor_behavior_from_specification()
    {
        string prompt = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "Prompts", "v3", "prompt.md"));

        Assert.Contains("Use internal reasoning first", prompt, StringComparison.Ordinal);
        Assert.Contains("Avoid tools whenever possible", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not ask clarifying questions", prompt, StringComparison.Ordinal);
        Assert.Contains("If uncertain, provide your best guess", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_prompt_content_is_rejected()
    {
        using PromptTestRepository repository = PromptTestRepository.Create();
        File.WriteAllText(Path.Combine(repository.Root, "Prompts", "v1", "prompt.md"), "  ");

        PromptValidationResult result = PromptArtifactValidator.Validate(repository.Root);

        Assert.Contains(result.Errors, error => error.Contains("prompt.md must not be empty", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_versions_are_rejected()
    {
        using PromptTestRepository repository = PromptTestRepository.Create();
        string metadataPath = Path.Combine(repository.Root, "Prompts", "v2", "metadata.json");
        string metadata = File.ReadAllText(metadataPath).Replace("\"v2\"", "\"v1\"", StringComparison.Ordinal);
        File.WriteAllText(metadataPath, metadata);

        PromptValidationResult result = PromptArtifactValidator.Validate(repository.Root);

        Assert.Contains(result.Errors, error => error.Contains("duplicate version 'v1'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Metrics_outside_percentage_range_are_rejected(int invalidMetric)
    {
        using PromptTestRepository repository = PromptTestRepository.Create();
        string metadataPath = Path.Combine(repository.Root, "Prompts", "v1", "metadata.json");
        string metadata = File.ReadAllText(metadataPath).Replace(
            "\"taskAdherence\": 82",
            $"\"taskAdherence\": {invalidMetric}",
            StringComparison.Ordinal);
        File.WriteAllText(metadataPath, metadata);

        PromptValidationResult result = PromptArtifactValidator.Validate(repository.Root);

        Assert.Contains(
            result.Errors,
            error => error.Contains("expectedMetrics.taskAdherence must be between 0 and 100", StringComparison.Ordinal));
    }

    [Fact]
    public void Malformed_fixture_is_rejected()
    {
        using PromptTestRepository repository = PromptTestRepository.Create();
        string fixturePath = Directory.GetFiles(
            Path.Combine(repository.Root, "EvaluationData"),
            "*.json").First();
        File.WriteAllText(fixturePath, """{"id":"broken","category":"account-access"}""");

        PromptValidationResult result = PromptArtifactValidator.Validate(repository.Root);

        Assert.Contains(result.Errors, error => error.Contains("userMessage is required", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("expected is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_json_is_reported_without_throwing()
    {
        using PromptTestRepository repository = PromptTestRepository.Create();
        File.WriteAllText(
            Path.Combine(repository.Root, "Prompts", "v1", "metadata.json"),
            "{not-json");

        PromptValidationResult result = PromptArtifactValidator.Validate(repository.Root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("invalid JSON", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GenAIOps.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private sealed class PromptTestRepository : IDisposable
    {
        private PromptTestRepository(string root) => Root = root;

        public string Root { get; }

        public static PromptTestRepository Create()
        {
            string source = FindRepositoryRoot();
            string root = Path.Combine(Path.GetTempPath(), $"genaiops-prompt-tests-{Guid.NewGuid():N}");
            CopyDirectory(Path.Combine(source, "Prompts"), Path.Combine(root, "Prompts"));
            CopyDirectory(Path.Combine(source, "EvaluationData"), Path.Combine(root, "EvaluationData"));
            CopyDirectory(Path.Combine(source, "Schemas"), Path.Combine(root, "Schemas"));
            return new PromptTestRepository(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }
    }
}
