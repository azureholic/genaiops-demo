using System.Collections.Concurrent;
using System.Globalization;
using GenAIOps.Application.Persistence;
using GenAIOps.Domain.Persistence;

namespace GenAIOps.Infrastructure.Persistence;

public sealed class InMemoryRepository<T> : IRepository<T>
    where T : class, IPersistedRecord
{
    private readonly ConcurrentDictionary<string, StoredItem<T>> items = new(StringComparer.Ordinal);

    public Task<StoredItem<T>?> GetAsync(
        string id,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        items.TryGetValue(GetKey(id, partitionKey), out StoredItem<T>? item);
        return Task.FromResult(item);
    }

    public Task<StoredItem<T>> CreateAsync(
        T item,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(item);
        StoredItem<T> stored = new(item, NewETag());
        if (!items.TryAdd(GetKey(item.Id, item.PartitionKey), stored))
        {
            throw new RecordConflictException(item.Type, item.Id, "a record with this id already exists.");
        }

        return Task.FromResult(stored);
    }

    public Task<StoredItem<T>> ReplaceAsync(
        T item,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(item);
        string key = GetKey(item.Id, item.PartitionKey);
        if (!items.TryGetValue(key, out StoredItem<T>? current))
        {
            throw new RecordNotFoundException(item.Type, item.Id, item.PartitionKey);
        }

        if (!string.Equals(current.ETag, expectedETag, StringComparison.Ordinal))
        {
            throw new RecordConflictException(item.Type, item.Id, "the supplied ETag is stale.");
        }

        StoredItem<T> replacement = new(item, NewETag());
        if (!items.TryUpdate(key, replacement, current))
        {
            throw new RecordConflictException(item.Type, item.Id, "the record was changed concurrently.");
        }

        return Task.FromResult(replacement);
    }

    public Task<RepositoryPage<T>> QueryAsync(
        RecordQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int pageSize = query.ValidatedPageSize;
        int offset = DecodeContinuationToken(query.ContinuationToken);
        StoredItem<T>[] matches = items.Values
            .Where(item =>
                string.Equals(item.Value.PartitionKey, query.PartitionKey, StringComparison.Ordinal)
                && (query.Type is null
                    || string.Equals(item.Value.Type, query.Type, StringComparison.Ordinal)))
            .OrderBy(item => item.Value.Id, StringComparer.Ordinal)
            .ToArray();
        StoredItem<T>[] page = matches.Skip(offset).Take(pageSize).ToArray();
        int nextOffset = offset + page.Length;
        string? continuationToken = nextOffset < matches.Length
            ? Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(
                    nextOffset.ToString(CultureInfo.InvariantCulture)))
            : null;

        return Task.FromResult(new RepositoryPage<T>(page, continuationToken, RequestCharge: 0));
    }

    private static string GetKey(string id, string partitionKey) => $"{partitionKey}\u001f{id}";

    private static string NewETag() => $"\"{Guid.NewGuid():N}\"";

    private static int DecodeContinuationToken(string? token)
    {
        if (token is null)
        {
            return 0;
        }

        try
        {
            string value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new ArgumentException("The continuation token is invalid.", nameof(token), exception);
        }
    }

    private static void Validate(T item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.PartitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Type);
        if (item.SchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(item), "Schema version must be positive.");
        }
    }
}
