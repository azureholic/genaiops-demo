using GenAIOps.Application.Chat;
using GenAIOps.Application.Shadow;
using GenAIOps.Infrastructure.Chat;
using GenAIOps.Infrastructure.Metrics;
using GenAIOps.Infrastructure.Persistence;
using GenAIOps.Infrastructure.Shadow;

var builder = Host.CreateApplicationBuilder(args);
string persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "InMemory";
if (string.Equals(persistenceProvider, "Cosmos", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddCosmosPersistence(
        new CosmosPersistenceOptions(
            builder.Configuration["Persistence:Cosmos:Endpoint"]
                ?? throw new InvalidOperationException("Persistence:Cosmos:Endpoint is required."),
            builder.Configuration["Persistence:Cosmos:DatabaseName"]
                ?? throw new InvalidOperationException(
                    "Persistence:Cosmos:DatabaseName is required."),
            builder.Configuration["Persistence:Cosmos:Key"]));
}
else
{
    builder.Services.AddInMemoryPersistence();
}

string datasetPath = builder.Configuration["ContinuousEvaluation:DatasetPath"]
    ?? Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "EvaluationData");
ContinuousEvaluationOptions continuousOptions = new(
    datasetPath,
    builder.Configuration["ContinuousEvaluation:RegistryId"] ?? "default",
    TimeSpan.FromMinutes(
        builder.Configuration.GetValue("ContinuousEvaluation:IntervalMinutes", 60)),
    TimeSpan.FromHours(
        builder.Configuration.GetValue("ContinuousEvaluation:WindowHours", 24)));
string continuousProvider = builder.Configuration["ContinuousEvaluation:Provider"]
    ?? (builder.Environment.IsDevelopment() ? "Fake" : "Foundry");
bool useFakeProvider =
    string.Equals(continuousProvider, "Fake", StringComparison.OrdinalIgnoreCase);
if (!useFakeProvider)
{
    string projectEndpoint = builder.Configuration["Chat:Foundry:ProjectEndpoint"]
        ?? throw new InvalidOperationException(
            "Chat:Foundry:ProjectEndpoint is required for continuous Foundry evaluation.");
    string evaluationEndpoint = builder.Configuration["Shadow:Foundry:EvaluationEndpoint"]
        ?? throw new InvalidOperationException(
            "Shadow:Foundry:EvaluationEndpoint is required for continuous Foundry evaluation.");
    builder.Services.AddFoundryChatGateway(
        new FoundryAgentOptions(
            new Uri(projectEndpoint, UriKind.Absolute),
            builder.Configuration["Chat:Foundry:ManagedIdentityClientId"]));
    builder.Services.AddSingleton(
        new FoundryEvaluationOptions(
            new Uri(evaluationEndpoint, UriKind.Absolute),
            builder.Configuration["Shadow:Foundry:TokenScope"] ?? "https://ai.azure.com/.default",
            builder.Configuration["Shadow:Foundry:ManagedIdentityClientId"]));
    builder.Services.AddSingleton<HttpClient>();
    builder.Services.AddSingleton<IFoundryEvaluationClient, FoundryEvaluationClient>();
    builder.Services.AddSingleton<IResponseEvaluator, FoundryResponseEvaluator>();
}

builder.Services.AddLocalContinuousEvaluation(continuousOptions, useFakeProvider);
builder.Services.AddHostedService<ContinuousEvaluationBackgroundService>();

var host = builder.Build();
host.Run();

static string FindRepositoryRoot(string startPath)
{
    DirectoryInfo? directory = new(Path.GetFullPath(startPath));
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "GenAIOps.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Repository root could not be found.");
}
