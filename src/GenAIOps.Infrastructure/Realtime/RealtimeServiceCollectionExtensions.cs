using GenAIOps.Application.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GenAIOps.Infrastructure.Realtime;

public static class RealtimeServiceCollectionExtensions
{
    public static IServiceCollection AddNoOpRealtimeUpdates(this IServiceCollection services)
    {
        services.TryAddSingleton<IRealtimePublisher>(NoOpRealtimePublisher.Instance);
        return services;
    }
}
