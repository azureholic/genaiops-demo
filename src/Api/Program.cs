using System.Text.Json.Serialization;
using GenAIOps.Application.Chat;
using GenAIOps.Application.Experiments;
using GenAIOps.Application.Metrics;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Application.Releases;
using GenAIOps.Application.Shadow;
using GenAIOps.Domain.Prompts;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;
using GenAIOps.Infrastructure.Chat;
using GenAIOps.Infrastructure.Metrics;
using GenAIOps.Infrastructure.Persistence;
using GenAIOps.Infrastructure.Releases;
using GenAIOps.Infrastructure.Shadow;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

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
string metricsRepositoryRoot = FindRepositoryRoot(builder.Environment.ContentRootPath)
    ?? throw new InvalidOperationException("Repository root could not be found.");
ContinuousEvaluationOptions continuousOptions = new(
    builder.Configuration["ContinuousEvaluation:DatasetPath"]
        ?? Path.Combine(metricsRepositoryRoot, "EvaluationData"),
    builder.Configuration["ContinuousEvaluation:RegistryId"] ?? "default",
    TimeSpan.FromMinutes(
        builder.Configuration.GetValue("ContinuousEvaluation:IntervalMinutes", 60)),
    TimeSpan.FromHours(
        builder.Configuration.GetValue("ContinuousEvaluation:WindowHours", 24)));
string continuousProvider = builder.Configuration["ContinuousEvaluation:Provider"]
    ?? (builder.Environment.IsDevelopment() ? "Fake" : "Foundry");
if (!string.Equals(continuousProvider, "Fake", StringComparison.OrdinalIgnoreCase)
    && !string.Equals(continuousProvider, "Foundry", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Unsupported continuous evaluation provider '{continuousProvider}'. "
        + "Use 'Fake' or 'Foundry'.");
}

builder.Services.AddLocalContinuousEvaluation(
    continuousOptions,
    useFakeProvider: string.Equals(
        continuousProvider,
        "Fake",
        StringComparison.OrdinalIgnoreCase));
builder.Services.AddReleaseWorkflows(
    new QualityGateOptions(
        builder.Configuration.GetValue("QualityGates:MinimumSamples", 3),
        builder.Configuration.GetValue("QualityGates:MinimumTaskAdherence", 0.9),
        builder.Configuration.GetValue("QualityGates:MinimumGroundedness", 0.9),
        builder.Configuration.GetValue("QualityGates:MinimumToolAccuracy", 0.9),
        builder.Configuration.GetValue("QualityGates:MaximumFailureRate", 0.05),
        builder.Configuration.GetValue<double?>("QualityGates:MaximumLatencyMilliseconds")));
if (!string.Equals(persistenceProvider, "Cosmos", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<ContinuousEvaluationBackgroundService>();
}

ShadowProcessingOptions shadowOptions = new(
    TimeSpan.FromSeconds(builder.Configuration.GetValue("Shadow:TimeoutSeconds", 30)),
    builder.Configuration.GetValue("Shadow:MaximumAttempts", 3));
string evaluationProvider = builder.Configuration["Shadow:EvaluationProvider"]
    ?? (builder.Environment.IsDevelopment() ? "Fake" : "Foundry");
if (string.Equals(evaluationProvider, "Fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddLocalShadowEvaluation(
        shadowOptions,
        usePersistentQueue: string.Equals(
            persistenceProvider,
            "Cosmos",
            StringComparison.OrdinalIgnoreCase));
}
else if (string.Equals(evaluationProvider, "Foundry", StringComparison.OrdinalIgnoreCase))
{
    string evaluationEndpoint = builder.Configuration["Shadow:Foundry:EvaluationEndpoint"]
        ?? throw new InvalidOperationException(
            "Shadow:Foundry:EvaluationEndpoint is required for Foundry evaluations.");
    builder.Services.AddFoundryShadowEvaluation(
        new FoundryEvaluationOptions(
            new Uri(evaluationEndpoint, UriKind.Absolute),
            builder.Configuration["Shadow:Foundry:TokenScope"] ?? "https://ai.azure.com/.default",
            builder.Configuration["Shadow:Foundry:ManagedIdentityClientId"]),
        shadowOptions,
        usePersistentQueue: true);
}
else
{
    throw new InvalidOperationException(
        $"Unsupported evaluation provider '{evaluationProvider}'. Use 'Fake' or 'Foundry'.");
}

if (!string.Equals(persistenceProvider, "Cosmos", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<ShadowEvaluationBackgroundService>();
}
builder.Services.AddSingleton<IChatService, ChatService>();
builder.Services.AddSingleton<IExperimentService, ExperimentService>();
builder.Services.AddSingleton<IExperimentRouter, ExperimentRouter>();

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
            ReleaseValidationException release => (
                StatusCodes.Status400BadRequest,
                "Invalid release command",
                release.Code),
            ReleaseStateNotFoundException release => (
                StatusCodes.Status404NotFound,
                "Release state not found",
                release.Code),
            ReleaseIdempotencyConflictException release => (
                StatusCodes.Status409Conflict,
                "Idempotency conflict",
                release.Code),
            ReleaseConcurrencyException release => (
                StatusCodes.Status412PreconditionFailed,
                "Registry precondition failed",
                release.Code),
            ExperimentValidationException experiment => (
                StatusCodes.Status400BadRequest,
                "Invalid experiment",
                experiment.Code),
            ExperimentNotFoundException experiment => (
                StatusCodes.Status404NotFound,
                "Experiment not found",
                experiment.Code),
            ExperimentEligibilityException experiment => (
                StatusCodes.Status422UnprocessableEntity,
                "Ineligible experiment version",
                experiment.Code),
            ExperimentConcurrencyException experiment => (
                StatusCodes.Status412PreconditionFailed,
                "Experiment precondition failed",
                experiment.Code),
            ExperimentLifecycleException experiment => (
                StatusCodes.Status409Conflict,
                "Invalid experiment transition",
                experiment.Code),
            QualityGateRejectedException release => (
                StatusCodes.Status422UnprocessableEntity,
                "Quality gate rejected",
                release.Code),
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
            CosmosException cosmos when cosmos.StatusCode == System.Net.HttpStatusCode.BadRequest => (
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

        if (exception is QualityGateRejectedException rejection)
        {
            problem.Extensions["gateEvidence"] = rejection.Evidence;
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
    "/api/abtest",
    async (
        AbTestApiRequest request,
        HttpContext context,
        IExperimentService experiments,
        CancellationToken cancellationToken) =>
    {
        if (!Enum.TryParse(
                request.Action,
                ignoreCase: true,
                out ExperimentCommandAction action))
        {
            throw new ExperimentValidationException(
                "Action must be start, update, or end.");
        }

        ExperimentResult result = await experiments.ExecuteAsync(
            new ExperimentCommand(
                string.IsNullOrWhiteSpace(request.RegistryId) ? "default" : request.RegistryId,
                action,
                request.Actor ?? string.Empty,
                request.Name,
                request.Allocations,
                context.Request.Headers.IfMatch.FirstOrDefault()),
            cancellationToken);
        context.Response.Headers.ETag = result.ETag;
        AbTestApiResponse response = new(result.Experiment, result.ETag);
        return action == ExperimentCommandAction.Start
            ? Results.Created("/api/abtest", response)
            : Results.Ok(response);
    })
    .WithName("ManageAbTest")
    .Produces<AbTestApiResponse>(StatusCodes.Status201Created)
    .Produces<AbTestApiResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status412PreconditionFailed)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

app.MapPost(
    "/api/promote/{version}",
    async (
        string version,
        ReleaseApiRequest request,
        HttpContext context,
        IReleaseWorkflowService workflows,
        CancellationToken cancellationToken) =>
    {
        ReleaseWorkflowResult result = await workflows.PromoteAsync(
            version,
            CreateReleaseCommand(request, context),
            cancellationToken);
        context.Response.Headers.ETag = result.Registry.ETag;
        return Results.Ok(ToReleaseResponse(result));
    })
    .WithName("PromoteVersion")
    .Produces<ReleaseApiResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status412PreconditionFailed)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

app.MapPost(
    "/api/rollback",
    async (
        ReleaseApiRequest request,
        HttpContext context,
        IReleaseWorkflowService workflows,
        CancellationToken cancellationToken) =>
    {
        ReleaseWorkflowResult result = await workflows.RollbackAsync(
            CreateReleaseCommand(request, context),
            cancellationToken);
        context.Response.Headers.ETag = result.Registry.ETag;
        return Results.Ok(ToReleaseResponse(result));
    })
    .WithName("RollbackVersion")
    .Produces<ReleaseApiResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status412PreconditionFailed);

app.MapGet(
    "/api/metrics",
    async (
        string? registryId,
        string? version,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? pageSize,
        string? continuationToken,
        IMetricsQueryService metrics,
        CancellationToken cancellationToken) =>
    {
        string resolvedRegistryId = string.IsNullOrWhiteSpace(registryId) ? "default" : registryId;
        MetricsPage page = await metrics.QueryAsync(
            new MetricsQuery(
                resolvedRegistryId,
                version,
                from,
                to,
                pageSize ?? 50,
                continuationToken),
            cancellationToken);
        return Results.Ok(
            new MetricsResponse(
                resolvedRegistryId,
                page.Items.Select(
                    snapshot => new MetricSnapshotResponse(
                        snapshot.Id,
                        snapshot.PromptVersion,
                        snapshot.WindowStart,
                        snapshot.WindowEnd,
                        snapshot.GeneratedAt,
                        snapshot.SampleCount,
                        snapshot.SuccessfulCount,
                        snapshot.FailureCount,
                        snapshot.Metrics)).ToArray(),
                page.ContinuationToken));
    })
    .WithName("GetMetrics")
    .Produces<MetricsResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapGet(
    "/api/evaluations",
    async (
        string? registryId,
        int? pageSize,
        string? continuationToken,
        IRepository<ShadowEvaluationRecord> evaluations,
        CancellationToken cancellationToken) =>
    {
        string resolvedRegistryId = string.IsNullOrWhiteSpace(registryId) ? "default" : registryId;
        RepositoryPage<ShadowEvaluationRecord> page = await evaluations.QueryAsync(
            new RecordQuery(
                resolvedRegistryId,
                Type: "shadowEvaluation",
                PageSize: pageSize ?? 50,
                ContinuationToken: continuationToken),
            cancellationToken);
        return Results.Ok(
            new EvaluationsResponse(
                resolvedRegistryId,
                page.Items.Select(
                    item => new EvaluationSummary(
                        item.Value.CorrelationId,
                        item.Value.ProductionAgentId,
                        item.Value.ProductionPromptVersion,
                        item.Value.CandidateAgentId,
                        item.Value.CandidatePromptVersion,
                        item.Value.Lifecycle,
                        item.Value.Scores,
                        item.Value.CandidateLatencyMilliseconds,
                        item.Value.AttemptCount,
                        item.Value.ErrorCode,
                        item.Value.ErrorMessage,
                        item.Value.CreatedAt,
                        item.Value.CompletedAt)).ToArray(),
                page.ContinuationToken));
    })
    .WithName("GetEvaluations")
    .Produces<EvaluationsResponse>()
    .ProducesProblem(StatusCodes.Status400BadRequest);

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
            cancellationToken,
            context.Request.Headers["X-Assignment-Key"].FirstOrDefault());
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
        RegistrySnapshot production = (await registry.GetAsync(registryId))!;
        await registry.RegisterCandidateAsync(
            registryId,
            builder.Configuration["Chat:LocalCandidate:AgentName"] ?? "local-support-candidate",
            builder.Configuration["Chat:LocalCandidate:AgentVersion"] ?? "v2",
            production.ETag);
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

static ReleaseCommand CreateReleaseCommand(ReleaseApiRequest request, HttpContext context)
{
    string idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault()
        ?? string.Empty;
    string expectedETag = context.Request.Headers.IfMatch.FirstOrDefault()
        ?? string.Empty;
    return new ReleaseCommand(
        string.IsNullOrWhiteSpace(request.RegistryId) ? "default" : request.RegistryId,
        request.Actor ?? string.Empty,
        idempotencyKey,
        expectedETag);
}

static ReleaseApiResponse ToReleaseResponse(ReleaseWorkflowResult result) =>
    new(
        result.Registry.State.Id,
        result.Registry.ETag,
        result.Registry.State.Production,
        result.Release,
        result.Replayed);

internal sealed record ChatApiRequest(string? Message, string? RegistryId = null);

internal sealed record ReleaseApiRequest(string? Actor, string? RegistryId = null);

internal sealed record AbTestApiRequest(
    string? Action,
    string? Actor,
    string? RegistryId = null,
    string? Name = null,
    IReadOnlyList<ExperimentAllocation>? Allocations = null);

internal sealed record AbTestApiResponse(ExperimentRecord Experiment, string ETag);

internal sealed record ReleaseApiResponse(
    string RegistryId,
    string ETag,
    AgentAssignment? Production,
    ReleaseRecord Release,
    bool Replayed);

internal sealed record EvaluationsResponse(
    string RegistryId,
    IReadOnlyList<EvaluationSummary> Evaluations,
    string? ContinuationToken);

internal sealed record EvaluationSummary(
    string CorrelationId,
    string ProductionAgentId,
    string ProductionPromptVersion,
    string CandidateAgentId,
    string CandidatePromptVersion,
    EvaluationLifecycle Lifecycle,
    IReadOnlyDictionary<string, double> Scores,
    long? CandidateLatencyMilliseconds,
    int AttemptCount,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

internal sealed record MetricsResponse(
    string RegistryId,
    IReadOnlyList<MetricSnapshotResponse> Snapshots,
    string? ContinuationToken);

internal sealed record MetricSnapshotResponse(
    string Id,
    string PromptVersion,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    DateTimeOffset GeneratedAt,
    int SampleCount,
    int SuccessfulCount,
    int FailureCount,
    IReadOnlyDictionary<string, double> Metrics);

internal sealed record VersionsResponse(
    string RegistryId,
    string ETag,
    AgentAssignment? Production,
    AgentAssignment? Candidate,
    IReadOnlyList<PromptVersionRecord> Versions,
    string? ContinuationToken);

public partial class Program;
