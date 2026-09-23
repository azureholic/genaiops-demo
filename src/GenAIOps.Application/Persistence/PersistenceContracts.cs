using GenAIOps.Domain.Persistence;

namespace GenAIOps.Application.Persistence;

public sealed record StoredItem<T>(T Value, string ETag)
    where T : class, IPersistedRecord;

public sealed record RepositoryPage<T>(
    IReadOnlyList<StoredItem<T>> Items,
    string? ContinuationToken,
    double RequestCharge)
    where T : class, IPersistedRecord;

public sealed record RecordQuery(
    string PartitionKey,
    string? Type = null,
    int PageSize = 50,
    string? ContinuationToken = null)
{
    public const int MaximumPageSize = 100;

    public int ValidatedPageSize =>
        PageSize is > 0 and <= MaximumPageSize
            ? PageSize
            : throw new ArgumentOutOfRangeException(
                nameof(PageSize),
                PageSize,
                $"Page size must be between 1 and {MaximumPageSize}.");
}

public interface IRepository<T>
    where T : class, IPersistedRecord
{
    Task<StoredItem<T>?> GetAsync(
        string id,
        string partitionKey,
        CancellationToken cancellationToken = default);

    Task<StoredItem<T>> CreateAsync(
        T item,
        CancellationToken cancellationToken = default);

    Task<StoredItem<T>> ReplaceAsync(
        T item,
        string expectedETag,
        CancellationToken cancellationToken = default);

    Task<RepositoryPage<T>> QueryAsync(
        RecordQuery query,
        CancellationToken cancellationToken = default);
}

public class PersistenceException : Exception
{
    public PersistenceException(string message)
        : base(message)
    {
    }

    public PersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RecordNotFoundException : PersistenceException
{
    public RecordNotFoundException(string recordType, string id, string partitionKey)
        : base($"{recordType} record '{id}' was not found in partition '{partitionKey}'.")
    {
    }
}

public sealed class RecordConflictException : PersistenceException
{
    public RecordConflictException(string recordType, string id, string message)
        : base($"{recordType} record '{id}' has a conflict: {message}")
    {
    }

    public RecordConflictException(string recordType, string id, string message, Exception innerException)
        : base($"{recordType} record '{id}' has a conflict: {message}", innerException)
    {
    }
}
