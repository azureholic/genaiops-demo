using GenAIOps.Application.Releases;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.Infrastructure.Releases;

public static class ReleaseServiceCollectionExtensions
{
    public static IServiceCollection AddReleaseWorkflows(
        this IServiceCollection services,
        QualityGateOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<IReleaseWorkflowService, ReleaseWorkflowService>();
        return services;
    }
}
