using GenAIOps.Application.Registry;
using GenAIOps.Application.Shadow;
using GenAIOps.Infrastructure.Chat;
using GenAIOps.Infrastructure.Observability;
using GenAIOps.Infrastructure.Persistence;
using GenAIOps.Infrastructure.Realtime;
using GenAIOps.Infrastructure.Shadow;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddGenAIOpsObservability(
    builder.Configuration,
    "genaiops-shadow-evaluator");
builder.Services.AddNoOpRealtimeUpdates();
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

builder.Services.AddSingleton<IAgentRegistryService, AgentRegistryService>();
string chatProvider = builder.Configuration["Chat:Provider"]
    ?? (builder.Environment.IsDevelopment() ? "Fake" : "Foundry");
if (string.Equals(chatProvider, "Fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddFakeChatGateway();
}
else
{
    string projectEndpoint = builder.Configuration["Chat:Foundry:ProjectEndpoint"]
        ?? throw new InvalidOperationException(
            "Chat:Foundry:ProjectEndpoint is required for the Foundry chat provider.");
    builder.Services.AddFoundryChatGateway(
        new FoundryAgentOptions(
            new Uri(projectEndpoint, UriKind.Absolute),
            builder.Configuration["Chat:Foundry:ManagedIdentityClientId"]));
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
else
{
    string endpoint = builder.Configuration["Shadow:Foundry:EvaluationEndpoint"]
        ?? throw new InvalidOperationException(
            "Shadow:Foundry:EvaluationEndpoint is required for Foundry evaluations.");
    builder.Services.AddFoundryShadowEvaluation(
        new FoundryEvaluationOptions(
            new Uri(endpoint, UriKind.Absolute),
            builder.Configuration["Shadow:Foundry:TokenScope"] ?? "https://ai.azure.com/.default",
            builder.Configuration["Shadow:Foundry:ManagedIdentityClientId"]),
        shadowOptions,
        usePersistentQueue: true);
}

builder.Services.AddHostedService<ShadowEvaluationBackgroundService>();

var host = builder.Build();
host.Run();
