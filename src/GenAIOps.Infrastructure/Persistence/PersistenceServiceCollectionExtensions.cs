using GenAIOps.Application.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryPersistence(this IServiceCollection services)
    {
        services.AddSingleton(typeof(IRepository<>), typeof(InMemoryRepository<>));
        return services;
    }

    public static IServiceCollection AddCosmosPersistence(
        this IServiceCollection services,
        CosmosPersistenceOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton(_ => CosmosClientFactory.Create(options));
        services.AddSingleton(typeof(IRepository<>), typeof(CosmosRepository<>));
        services.AddSingleton<CosmosStoreInitializer>();
        return services;
    }
}
