namespace GenAIOps.IntegrationTests;

public sealed class DocumentationTests
{
    [Fact]
    public void Runbook_contains_validated_commands_and_required_operational_guidance()
    {
        string root = FindRepositoryRoot();
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        string runbook = File.ReadAllText(Path.Combine(root, "docs", "demo-runbook.md"));

        string[] requiredFiles =
        [
            @"scripts\Invoke-EndToEndDemo.ps1",
            @"NuGet.config",
            @"src\Web\.npmrc",
            @"Infrastructure\main.bicep",
            @".github\workflows\README.md",
        ];
        foreach (string path in requiredFiles)
        {
            Assert.True(File.Exists(Path.Combine(root, path)), $"Documented path is missing: {path}");
        }

        string[] requiredRunbookText =
        [
            "under 15 minutes",
            "v1 baseline",
            "v2 shadow",
            "90% v2 / 10% v1",
            "Poor v3",
            "Rollback",
            "Accessibility",
            "Telemetry",
            "Troubleshooting",
            "Cleanup and cost controls",
            "dotnet restore GenAIOps.slnx --configfile NuGet.config",
            "npm --prefix src\\Web ci --userconfig src\\Web\\.npmrc",
            "az bicep build --file Infrastructure\\main.bicep",
            "npm --prefix src\\Web run validate:workflows",
            ".\\scripts\\Invoke-EndToEndDemo.ps1",
        ];
        foreach (string value in requiredRunbookText)
        {
            Assert.Contains(value, runbook, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Local development", readme, StringComparison.Ordinal);
        Assert.Contains("package proxies", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("client secret value", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client secret value", runbook, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GenAIOps.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
