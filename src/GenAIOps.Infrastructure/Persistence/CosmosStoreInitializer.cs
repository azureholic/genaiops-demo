using Microsoft.Azure.Cosmos;

namespace GenAIOps.Infrastructure.Persistence;

public sealed class CosmosStoreInitializer(
    CosmosClient client,
    CosmosPersistenceOptions options)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();
        DatabaseResponse database = await client.CreateDatabaseIfNotExistsAsync(
            options.DatabaseName,
            cancellationToken: cancellationToken);
        foreach (CosmosContainerDefinition definition in CosmosContainerDefinitions.All)
        {
            await database.Database.CreateContainerIfNotExistsAsync(
                definition.CreateProperties(),
                cancellationToken: cancellationToken);
        }
    }
}
