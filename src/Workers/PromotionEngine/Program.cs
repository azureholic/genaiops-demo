using GenAIOps.Application.Metrics;
using GenAIOps.Application.Registry;
using GenAIOps.Infrastructure.Metrics;
using GenAIOps.Infrastructure.Observability;
using GenAIOps.Infrastructure.Persistence;
using GenAIOps.Infrastructure.Realtime;
using GenAIOps.Infrastructure.Releases;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddGenAIOpsObservability(
    builder.Configuration,
    "genaiops-promotion-engine");
builder.Services.AddNoOpRealtimeUpdates();
AddPersistence(builder);
builder.Services.AddSingleton<IAgentRegistryService, AgentRegistryService>();
builder.Services.AddSingleton<IMetricsQueryService, MetricsQueryService>();
builder.Services.AddReleaseWorkflows(ReadGates(builder.Configuration));
builder.Services.AddSingleton(
    new PromotionWorkerOptions(
        builder.Configuration.GetValue("Promotion:Enabled", false),
        builder.Configuration["Promotion:RegistryId"] ?? "default",
        builder.Configuration["Promotion:Version"] ?? string.Empty,
        builder.Configuration["Promotion:Actor"] ?? "promotion-worker",
        builder.Configuration["Promotion:IdempotencyKey"] ?? string.Empty,
        builder.Configuration["Promotion:ExpectedETag"] ?? string.Empty));
builder.Services.AddHostedService<PromotionCommandWorker>();

var host = builder.Build();
host.Run();

static void AddPersistence(HostApplicationBuilder builder)
{
    string provider = builder.Configuration["Persistence:Provider"] ?? "InMemory";
    if (string.Equals(provider, "Cosmos", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddCosmosPersistence(
            new CosmosPersistenceOptions(
                builder.Configuration["Persistence:Cosmos:Endpoint"]
                    ?? throw new InvalidOperationException(
                        "Persistence:Cosmos:Endpoint is required."),
                builder.Configuration["Persistence:Cosmos:DatabaseName"]
                    ?? throw new InvalidOperationException(
                        "Persistence:Cosmos:DatabaseName is required."),
                builder.Configuration["Persistence:Cosmos:Key"]));
    }
    else
    {
        builder.Services.AddInMemoryPersistence();
    }
}

static GenAIOps.Application.Releases.QualityGateOptions ReadGates(IConfiguration configuration) =>
    new(
        configuration.GetValue("QualityGates:MinimumSamples", 3),
        configuration.GetValue("QualityGates:MinimumTaskAdherence", 0.9),
        configuration.GetValue("QualityGates:MinimumGroundedness", 0.9),
        configuration.GetValue("QualityGates:MinimumToolAccuracy", 0.9),
        configuration.GetValue("QualityGates:MaximumFailureRate", 0.05),
        configuration.GetValue<double?>("QualityGates:MaximumLatencyMilliseconds"));
