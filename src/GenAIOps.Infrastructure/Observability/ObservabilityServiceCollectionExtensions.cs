using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using GenAIOps.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GenAIOps.Infrastructure.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddGenAIOpsObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        bool azureMonitorEnabled =
            configuration.GetValue("Observability:AzureMonitor:Enabled", false);
        string? connectionString =
            configuration["Observability:AzureMonitor:ConnectionString"];
        if (azureMonitorEnabled && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Observability:AzureMonitor:ConnectionString is required when "
                + "Observability:AzureMonitor:Enabled is true.");
        }

        ResourceBuilder resource = ResourceBuilder.CreateDefault().AddService(serviceName);
        services
            .AddOpenTelemetry()
            .ConfigureResource(builder => builder.AddService(serviceName))
            .WithTracing(builder =>
            {
                builder
                    .AddSource(GenAIOpsTelemetry.ActivitySourceName, "Azure.*")
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = false;
                        options.EnrichWithHttpRequest = static (activity, _) =>
                            RemoveSensitiveHttpTags(activity);
                        options.EnrichWithHttpResponse = static (activity, _) =>
                            RemoveSensitiveHttpTags(activity);
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = false;
                        options.EnrichWithHttpRequestMessage = static (activity, _) =>
                            RemoveSensitiveHttpTags(activity);
                        options.EnrichWithHttpResponseMessage = static (activity, _) =>
                            RemoveSensitiveHttpTags(activity);
                    });
                if (azureMonitorEnabled)
                {
                    builder.AddAzureMonitorTraceExporter(options =>
                        options.ConnectionString = connectionString);
                }
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddMeter(GenAIOpsTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
                if (azureMonitorEnabled)
                {
                    builder.AddAzureMonitorMetricExporter(options =>
                        options.ConnectionString = connectionString);
                }
            });

        services.AddLogging(builder => builder.AddOpenTelemetry());
        services.Configure<OpenTelemetryLoggerOptions>(
            options =>
            {
                options.SetResourceBuilder(resource);
                options.IncludeFormattedMessage = false;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                if (azureMonitorEnabled)
                {
                    options.AddAzureMonitorLogExporter(exporter =>
                        exporter.ConnectionString = connectionString);
                }
            });
        return services;
    }

    private static void RemoveSensitiveHttpTags(Activity activity)
    {
        activity.SetTag("url.full", null);
        activity.SetTag("url.query", null);
        activity.SetTag("http.request.header", null);
        activity.SetTag("http.response.header", null);
        activity.SetTag("server.address", null);
        activity.SetTag("client.address", null);
        activity.SetTag("user_agent.original", null);
    }
}
