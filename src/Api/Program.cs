using GenAIOps.Domain.Prompts;

if (args is ["validate-prompts"])
{
    string? repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
    if (repositoryRoot is null)
    {
        Console.Error.WriteLine("Prompt validation failed: repository root could not be found.");
        Environment.ExitCode = 1;
        return;
    }

    PromptValidationResult validation = PromptArtifactValidator.Validate(repositoryRoot);
    if (!validation.IsValid)
    {
        Console.Error.WriteLine("Prompt validation failed:");
        foreach (string error in validation.Errors)
        {
            Console.Error.WriteLine($"- {error}");
        }

        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine("Prompt validation succeeded: 3 prompt versions and 3 evaluation fixtures are valid.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();

static string? FindRepositoryRoot(string startPath)
{
    DirectoryInfo? directory = new(Path.GetFullPath(startPath));
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "GenAIOps.slnx"))
            && Directory.Exists(Path.Combine(directory.FullName, "Prompts"))
            && Directory.Exists(Path.Combine(directory.FullName, "EvaluationData")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}

public partial class Program;
