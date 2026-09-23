using System.Text.Json.Serialization;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Domain.Prompts;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;
using GenAIOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

string persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "InMemory";
if (string.Equals(persistenceProvider, "Cosmos", StringComparison.OrdinalIgnoreCase))
{
    CosmosPersistenceOptions cosmosOptions = new(
        builder.Configuration["Persistence:Cosmos:Endpoint"]
            ?? throw new InvalidOperationException("Persistence:Cosmos:Endpoint is required."),
        builder.Configuration["Persistence:Cosmos:DatabaseName"]
            ?? throw new InvalidOperationException("Persistence:Cosmos:DatabaseName is required."),
        builder.Configuration["Persistence:Cosmos:Key"]);
    builder.Services.AddCosmosPersistence(cosmosOptions);
}
else if (string.Equals(persistenceProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddInMemoryPersistence();
}
else
{
    throw new InvalidOperationException(
        $"Unsupported persistence provider '{persistenceProvider}'. Use 'InMemory' or 'Cosmos'.");
}

builder.Services.AddSingleton<IAgentRegistryService, AgentRegistryService>();
var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        Exception? exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        (int status, string title) = exception switch
        {
            RecordNotFoundException => (StatusCodes.Status404NotFound, "Record not found"),
            RecordConflictException => (StatusCodes.Status409Conflict, "Persistence conflict"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error"),
        };
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : exception?.Message,
        });
    });
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapGet(
    "/api/versions",
    async (
        string? registryId,
        int? pageSize,
        string? continuationToken,
        IAgentRegistryService registry,
        IRepository<PromptVersionRecord> versions,
        CancellationToken cancellationToken) =>
    {
        string resolvedRegistryId = string.IsNullOrWhiteSpace(registryId) ? "default" : registryId;
        RegistrySnapshot? snapshot = await registry.GetAsync(resolvedRegistryId, cancellationToken);
        if (snapshot is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Registry not found",
                detail: $"Agent registry '{resolvedRegistryId}' was not found.");
        }

        RepositoryPage<PromptVersionRecord> versionPage = await versions.QueryAsync(
            new RecordQuery(
                resolvedRegistryId,
                Type: "promptVersion",
                PageSize: pageSize ?? 50,
                ContinuationToken: continuationToken),
            cancellationToken);
        VersionsResponse response = new(
            resolvedRegistryId,
            snapshot.ETag,
            snapshot.State.Production,
            snapshot.State.Candidate,
            versionPage.Items.Select(item => item.Value).ToArray(),
            versionPage.ContinuationToken);
        return Results.Ok(response);
    })
    .WithName("GetVersions")
    .Produces<VersionsResponse>()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status400BadRequest);

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

internal sealed record VersionsResponse(
    string RegistryId,
    string ETag,
    AgentAssignment? Production,
    AgentAssignment? Candidate,
    IReadOnlyList<PromptVersionRecord> Versions,
    string? ContinuationToken);

public partial class Program;
