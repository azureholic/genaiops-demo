using System.Text.Json.Serialization;
using GenAIOps.Application.Chat;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Domain.Prompts;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;
using GenAIOps.Infrastructure.Chat;
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
builder.Services.AddSingleton<IChatService, ChatService>();

string chatProvider = builder.Configuration["Chat:Provider"]
    ?? (builder.Environment.IsDevelopment() ? "Fake" : "Foundry");
if (string.Equals(chatProvider, "Fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddFakeChatGateway();
}
else if (string.Equals(chatProvider, "Foundry", StringComparison.OrdinalIgnoreCase))
{
    string endpoint = builder.Configuration["Chat:Foundry:ProjectEndpoint"]
        ?? throw new InvalidOperationException(
            "Chat:Foundry:ProjectEndpoint is required for the Foundry chat provider.");
    builder.Services.AddFoundryChatGateway(
        new FoundryAgentOptions(
            new Uri(endpoint, UriKind.Absolute),
            builder.Configuration["Chat:Foundry:ManagedIdentityClientId"]));
}
else
{
    throw new InvalidOperationException(
        $"Unsupported chat provider '{chatProvider}'. Use 'Fake' or 'Foundry'.");
}

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        Exception? exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        (int status, string title, string? code) = exception switch
        {
            ChatValidationException chat => (
                StatusCodes.Status400BadRequest,
                "Invalid chat request",
                chat.Code),
            ProductionAgentNotFoundException chat => (
                StatusCodes.Status404NotFound,
                "Production agent not found",
                chat.Code),
            ChatProviderException chat => (
                StatusCodes.Status502BadGateway,
                "Chat provider failure",
                chat.Code),
            ChatRequestCanceledException chat => (
                499,
                "Chat request cancelled",
                chat.Code),
            ChatMetadataPersistenceException chat => (
                StatusCodes.Status503ServiceUnavailable,
                "Chat metadata persistence failure",
                chat.Code),
            BadHttpRequestException => (
                StatusCodes.Status400BadRequest,
                "Invalid chat request",
                "invalid_request"),
            RecordNotFoundException => (
                StatusCodes.Status404NotFound,
                "Record not found",
                null),
            RecordConflictException => (
                StatusCodes.Status409Conflict,
                "Persistence conflict",
                null),
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                null),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Unexpected server error",
                null),
        };
        context.Response.StatusCode = status;
        ProblemDetails problem = new()
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : exception?.Message,
        };
        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        string correlationId = context.Items["CorrelationId"] as string
            ?? context.TraceIdentifier;
        problem.Extensions["correlationId"] = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        await context.Response.WriteAsJsonAsync(problem);
    });
});

app.Use(
    async (context, next) =>
    {
        string correlationId = ResolveCorrelationId(context);
        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        await next(context);
    });

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapPost(
    "/api/chat",
    async (
        ChatApiRequest request,
        HttpContext context,
        IChatService chatService,
        CancellationToken cancellationToken) =>
    {
        string correlationId = (string)context.Items["CorrelationId"]!;
        ChatResponse response = await chatService.SendAsync(
            string.IsNullOrWhiteSpace(request.RegistryId) ? "default" : request.RegistryId,
            request.Message ?? string.Empty,
            correlationId,
            cancellationToken);
        return Results.Ok(response);
    })
    .WithName("PostChat")
    .Produces<ChatResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(499);

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

if (builder.Configuration.GetValue<bool>("Chat:SeedLocalProduction"))
{
    using IServiceScope scope = app.Services.CreateScope();
    IAgentRegistryService registry =
        scope.ServiceProvider.GetRequiredService<IAgentRegistryService>();
    string registryId = builder.Configuration["Chat:LocalProduction:RegistryId"] ?? "default";
    if (await registry.GetAsync(registryId) is null)
    {
        string agentName =
            builder.Configuration["Chat:LocalProduction:AgentName"] ?? "local-support-agent";
        string agentVersion =
            builder.Configuration["Chat:LocalProduction:AgentVersion"] ?? "v1";
        RegistrySnapshot candidate =
            await registry.RegisterCandidateAsync(registryId, agentName, agentVersion);
        await registry.PromoteCandidateAsync(
            registryId,
            agentName,
            candidate.ETag);
    }
}

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

static string ResolveCorrelationId(HttpContext context)
{
    string? supplied = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(supplied))
    {
        return supplied.Trim();
    }

    string? traceId = System.Diagnostics.Activity.Current?.TraceId.ToString();
    return string.IsNullOrWhiteSpace(traceId) ? Guid.NewGuid().ToString("N") : traceId;
}

internal sealed record ChatApiRequest(string? Message, string? RegistryId = null);

internal sealed record VersionsResponse(
    string RegistryId,
    string ETag,
    AgentAssignment? Production,
    AgentAssignment? Candidate,
    IReadOnlyList<PromptVersionRecord> Versions,
    string? ContinuationToken);

public partial class Program;
