using GenAIOps.Application.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.Infrastructure.Metrics;

public static class MetricsServiceCollectionExtensions
{
    public static IServiceCollection AddLocalContinuousEvaluation(
        this IServiceCollection services,
        ContinuousEvaluationOptions options,
        bool useFakeProvider = true)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<IMetricsAggregator, MetricsAggregator>();
        services.AddSingleton<IMetricsQueryService, MetricsQueryService>();
        if (useFakeProvider)
        {
            services.AddSingleton<IContinuousEvaluationProvider, FakeContinuousEvaluationProvider>();
        }
        else
        {
            services.AddSingleton<IContinuousEvaluationProvider, GatewayContinuousEvaluationProvider>();
        }
        services.AddSingleton<IContinuousEvaluationRunner, ContinuousEvaluationRunner>();
        return services;
    }
}
