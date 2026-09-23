using System.Text;
using System.Text.Json;
using GenAIOps.Application.Persistence;
using GenAIOps.Domain.Records;
using GenAIOps.Infrastructure.Persistence;

namespace GenAIOps.UnitTests;

public sealed class PersistenceSerializationTests
{
    [Fact]
    public void Cosmos_serializer_writes_schema_discriminator_and_string_enums()
    {
        PromptVersionRecord record = CreateVersion("v2", PromptVersionLifecycle.Candidate);
        SystemTextJsonCosmosSerializer serializer = new();

        using Stream stream = serializer.ToStream(record);
        using JsonDocument document = JsonDocument.Parse(stream);

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("promptVersion", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("candidate", document.RootElement.GetProperty("lifecycle").GetString());
        Assert.Equal("registry-a", document.RootElement.GetProperty("partitionKey").GetString());
    }

    [Fact]
    public void Cosmos_serializer_round_trips_domain_record()
    {
        PromptVersionRecord expected = CreateVersion("v1", PromptVersionLifecycle.Production);
        SystemTextJsonCosmosSerializer serializer = new();

        using Stream serialized = serializer.ToStream(expected);
        PromptVersionRecord actual = serializer.FromStream<PromptVersionRecord>(serialized);

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.PartitionKey, actual.PartitionKey);
        Assert.Equal(expected.ContentHash, actual.ContentHash);
        Assert.Equal(expected.ExpectedMetrics, actual.ExpectedMetrics);
        Assert.Equal(PromptVersionLifecycle.Production, actual.Lifecycle);
    }

    private static PromptVersionRecord CreateVersion(
        string version,
        PromptVersionLifecycle lifecycle) =>
        new(
            version,
            "registry-a",
            version,
            $"Version {version}",
            "sha256:test",
            lifecycle,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            new Dictionary<string, double> { ["groundedness"] = 0.97 });
}

public sealed class InMemoryPersistenceTests
{
    [Fact]
    public async Task Query_is_partition_scoped_parameter_shaped_and_paginated()
    {
        InMemoryRepository<PromptVersionRecord> repository = new();
        await repository.CreateAsync(CreateVersion("v1", "registry-a"));
        await repository.CreateAsync(CreateVersion("v2", "registry-a"));
        await repository.CreateAsync(CreateVersion("v3", "registry-a"));
        await repository.CreateAsync(CreateVersion("other", "registry-b"));

        RepositoryPage<PromptVersionRecord> first = await repository.QueryAsync(
            new RecordQuery("registry-a", "promptVersion", PageSize: 2));
        RepositoryPage<PromptVersionRecord> second = await repository.QueryAsync(
            new RecordQuery(
                "registry-a",
                "promptVersion",
                PageSize: 2,
                ContinuationToken: first.ContinuationToken));

        Assert.Equal(["v1", "v2"], first.Items.Select(item => item.Value.Id));
        Assert.NotNull(first.ContinuationToken);
        Assert.Equal("v3", Assert.Single(second.Items).Value.Id);
        Assert.Null(second.ContinuationToken);
    }

    [Fact]
    public async Task Replace_reports_not_found_explicitly()
    {
        InMemoryRepository<PromptVersionRecord> repository = new();

        RecordNotFoundException exception = await Assert.ThrowsAsync<RecordNotFoundException>(
            () => repository.ReplaceAsync(CreateVersion("missing", "registry-a"), "\"etag\""));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("registry-a", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_replaces_allow_exactly_one_ETag_winner()
    {
        InMemoryRepository<PromptVersionRecord> repository = new();
        StoredItem<PromptVersionRecord> original =
            await repository.CreateAsync(CreateVersion("v1", "registry-a"));
        PromptVersionRecord update = original.Value with { DisplayName = "Updated" };

        Task<Exception?> first = Record.ExceptionAsync(
            () => Task.Run(() => repository.ReplaceAsync(update, original.ETag)));
        Task<Exception?> second = Record.ExceptionAsync(
            () => Task.Run(() => repository.ReplaceAsync(update, original.ETag)));
        Exception?[] results = await Task.WhenAll(first, second);

        Assert.Single(results, exception => exception is null);
        Assert.Single(results, exception => exception is RecordConflictException);
    }

    [Fact]
    public async Task Create_rejects_duplicate_identity()
    {
        InMemoryRepository<PromptVersionRecord> repository = new();
        PromptVersionRecord version = CreateVersion("v1", "registry-a");
        await repository.CreateAsync(version);

        await Assert.ThrowsAsync<RecordConflictException>(() => repository.CreateAsync(version));
    }

    private static PromptVersionRecord CreateVersion(string id, string partitionKey) =>
        new(
            id,
            partitionKey,
            id,
            id,
            Convert.ToHexString(Encoding.UTF8.GetBytes(id)),
            PromptVersionLifecycle.Draft,
            DateTimeOffset.UtcNow,
            new Dictionary<string, double>());
}

public sealed class CosmosPersistenceDefinitionTests
{
    [Fact]
    public void Every_container_uses_query_aligned_partition_key_and_explicit_indexes()
    {
        Assert.Equal(5, CosmosContainerDefinitions.All.Count);
        Assert.All(
            CosmosContainerDefinitions.All,
            definition =>
            {
                Assert.Equal("/partitionKey", definition.PartitionKeyPath);
                Microsoft.Azure.Cosmos.ContainerProperties properties = definition.CreateProperties();
                Assert.Contains(properties.IndexingPolicy.IncludedPaths, path => path.Path == "/*");
                Assert.Contains(
                    properties.IndexingPolicy.CompositeIndexes.SelectMany(index => index),
                    path => path.Path == "/type");
            });
    }

    [Fact]
    public void Record_types_are_mapped_to_intent_specific_containers()
    {
        Assert.Equal("registry", CosmosContainerDefinitions.For<PromptVersionRecord>().Name);
        Assert.Equal("evaluations", CosmosContainerDefinitions.For<EvaluationRecord>().Name);
        Assert.Equal("metrics", CosmosContainerDefinitions.For<MetricSnapshotRecord>().Name);
        Assert.Equal("experiments", CosmosContainerDefinitions.For<ExperimentRecord>().Name);
    }
}
