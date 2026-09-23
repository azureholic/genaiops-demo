using System.Net;
using System.Text.Json;
using GenAIOps.Application.Persistence;
using GenAIOps.Domain.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace GenAIOps.Infrastructure.Persistence;

public sealed record CosmosPersistenceOptions(
    string Endpoint,
    string DatabaseName,
    string? Key = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(DatabaseName);
    }
}

public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosPersistenceOptions options)
    {
        options.Validate();
        CosmosClientOptions clientOptions = new()
        {
            ApplicationName = "GenAIOps",
            ConnectionMode = ConnectionMode.Direct,
            Serializer = new SystemTextJsonCosmosSerializer(),
        };

        return string.IsNullOrWhiteSpace(options.Key)
            ? new CosmosClient(options.Endpoint, clientOptions)
            : new CosmosClient(options.Endpoint, options.Key, clientOptions);
    }
}

public sealed class CosmosRepository<T>(
    CosmosClient client,
    CosmosPersistenceOptions options,
    ILogger<CosmosRepository<T>> logger) : IRepository<T>
    where T : class, IPersistedRecord
{
    private readonly Container container =
        client.GetContainer(options.DatabaseName, CosmosContainerDefinitions.For<T>().Name);

    public async Task<StoredItem<T>?> GetAsync(
        string id,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ItemResponse<T> response = await container.ReadItemAsync<T>(
                id,
                new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);
            LogDiagnostics("read", response.RequestCharge);
            return new StoredItem<T>(response.Resource, response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation(
                "Cosmos read did not find a {RecordType} record.",
                typeof(T).Name);
            return null;
        }
    }

    public async Task<StoredItem<T>> CreateAsync(
        T item,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ItemResponse<T> response = await container.CreateItemAsync(
                item,
                new PartitionKey(item.PartitionKey),
                cancellationToken: cancellationToken);
            LogDiagnostics("create", response.RequestCharge);
            return new StoredItem<T>(response.Resource, response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            throw new RecordConflictException(
                item.Type,
                item.Id,
                CosmosFailureMessage(exception),
                exception);
        }
    }

    public async Task<StoredItem<T>> ReplaceAsync(
        T item,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ItemRequestOptions requestOptions = new() { IfMatchEtag = expectedETag };
            ItemResponse<T> response = await container.ReplaceItemAsync(
                item,
                item.Id,
                new PartitionKey(item.PartitionKey),
                requestOptions,
                cancellationToken);
            LogDiagnostics("replace", response.RequestCharge);
            return new StoredItem<T>(response.Resource, response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new RecordNotFoundException(item.Type, item.Id, item.PartitionKey);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new RecordConflictException(
                item.Type,
                item.Id,
                CosmosFailureMessage(exception),
                exception);
        }
    }

    public async Task<RepositoryPage<T>> QueryAsync(
        RecordQuery query,
        CancellationToken cancellationToken = default)
    {
        int pageSize = query.ValidatedPageSize;
        QueryDefinition definition = query.Type is null
            ? new QueryDefinition(
                "SELECT * FROM c WHERE c.partitionKey = @partitionKey ORDER BY c.id")
                .WithParameter("@partitionKey", query.PartitionKey)
            : new QueryDefinition(
                "SELECT * FROM c WHERE c.partitionKey = @partitionKey AND c.type = @type ORDER BY c.id")
                .WithParameter("@partitionKey", query.PartitionKey)
                .WithParameter("@type", query.Type);
        QueryRequestOptions requestOptions = new()
        {
            MaxItemCount = pageSize,
            PartitionKey = new PartitionKey(query.PartitionKey),
        };
        using FeedIterator<JsonElement> iterator = container.GetItemQueryIterator<JsonElement>(
            definition,
            query.ContinuationToken,
            requestOptions);
        if (!iterator.HasMoreResults)
        {
            return new RepositoryPage<T>([], null, RequestCharge: 0);
        }

        FeedResponse<JsonElement> response = await iterator.ReadNextAsync(cancellationToken);
        logger.LogInformation(
            "Cosmos query for {RecordType} consumed {RequestCharge} RU.",
            typeof(T).Name,
            response.RequestCharge);
        StoredItem<T>[] records = response
            .Select(item =>
            {
                T value = item.Deserialize<T>(SystemTextJsonCosmosSerializer.DefaultOptions)
                    ?? throw new JsonException($"Cosmos returned an empty {typeof(T).Name} document.");
                string etag = item.TryGetProperty("_etag", out JsonElement etagProperty)
                    ? etagProperty.GetString() ?? string.Empty
                    : string.Empty;
                return new StoredItem<T>(value, etag);
            })
            .ToArray();
        return new RepositoryPage<T>(records, response.ContinuationToken, response.RequestCharge);
    }

    private static string CosmosFailureMessage(CosmosException exception) =>
        $"Cosmos returned {(int)exception.StatusCode} ({exception.StatusCode}).";

    private void LogDiagnostics(
        string operation,
        double requestCharge) =>
        logger.LogInformation(
            "Cosmos {Operation} for {RecordType} consumed {RequestCharge} RU.",
            operation,
            typeof(T).Name,
            requestCharge);
}
