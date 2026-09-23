using GenAIOps.Application.Shadow;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.Infrastructure.Shadow;

public static class ShadowServiceCollectionExtensions
{
    public static IServiceCollection AddLocalShadowEvaluation(
        this IServiceCollection services,
        ShadowProcessingOptions? options = null,
        bool usePersistentQueue = false)
    {
        AddQueue(services, usePersistentQueue);
        services.AddSingleton<IShadowWorkPublisher>(
            provider => provider.GetRequiredService<IShadowWorkQueue>());
        services.AddSingleton<IResponseEvaluator, FakeResponseEvaluator>();
        services.AddSingleton(options ?? ShadowProcessingOptions.Default);
        services.AddSingleton<IShadowEvaluationProcessor, ShadowEvaluationProcessor>();
        return services;
    }

    public static IServiceCollection AddFoundryShadowEvaluation(
        this IServiceCollection services,
        FoundryEvaluationOptions foundryOptions,
        ShadowProcessingOptions? processingOptions = null,
        bool usePersistentQueue = true)
    {
        AddQueue(services, usePersistentQueue);
        services.AddSingleton<IShadowWorkPublisher>(
            provider => provider.GetRequiredService<IShadowWorkQueue>());
        services.AddSingleton(foundryOptions);
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IFoundryEvaluationClient, FoundryEvaluationClient>();
        services.AddSingleton<IResponseEvaluator, FoundryResponseEvaluator>();
        services.AddSingleton(processingOptions ?? ShadowProcessingOptions.Default);
        services.AddSingleton<IShadowEvaluationProcessor, ShadowEvaluationProcessor>();
        return services;
    }

    private static void AddQueue(IServiceCollection services, bool usePersistentQueue)
    {
        if (usePersistentQueue)
        {
            services.AddSingleton<IShadowWorkQueue, PersistentShadowWorkQueue>();
        }
        else
        {
            services.AddSingleton<IShadowWorkQueue, InMemoryShadowWorkQueue>();
        }
    }
}
