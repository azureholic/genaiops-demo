namespace GenAIOps.Domain.Persistence;

public interface IPersistedRecord
{
    string Id { get; }

    string PartitionKey { get; }

    int SchemaVersion { get; }

    string Type { get; }
}
