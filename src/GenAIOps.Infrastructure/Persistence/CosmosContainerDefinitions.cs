using GenAIOps.Domain.Persistence;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;
using Microsoft.Azure.Cosmos;

namespace GenAIOps.Infrastructure.Persistence;

public sealed record CosmosContainerDefinition(string Name, string PartitionKeyPath)
{
    public ContainerProperties CreateProperties()
    {
        ContainerProperties properties = new(Name, PartitionKeyPath)
        {
            IndexingPolicy = new IndexingPolicy
            {
                IndexingMode = IndexingMode.Consistent,
                Automatic = true,
            },
        };
        properties.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = "/*" });
        properties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/\"_etag\"/?" });
        properties.IndexingPolicy.CompositeIndexes.Add(
        [
            new CompositePath { Path = "/partitionKey", Order = CompositePathSortOrder.Ascending },
            new CompositePath { Path = "/type", Order = CompositePathSortOrder.Ascending },
            new CompositePath { Path = "/id", Order = CompositePathSortOrder.Ascending },
        ]);
        return properties;
    }
}

public static class CosmosContainerDefinitions
{
    public static readonly CosmosContainerDefinition Registry = new("registry", "/partitionKey");
    public static readonly CosmosContainerDefinition Deployments = new("deployments", "/partitionKey");
    public static readonly CosmosContainerDefinition Evaluations = new("evaluations", "/partitionKey");
    public static readonly CosmosContainerDefinition Metrics = new("metrics", "/partitionKey");
    public static readonly CosmosContainerDefinition Experiments = new("experiments", "/partitionKey");
    public static readonly CosmosContainerDefinition Requests = new("requests", "/partitionKey");
    public static readonly CosmosContainerDefinition ShadowWork = new("shadow-work", "/partitionKey");

    public static IReadOnlyList<CosmosContainerDefinition> All { get; } =
        [Registry, Deployments, Evaluations, Metrics, Experiments, Requests, ShadowWork];

    public static CosmosContainerDefinition For<T>()
        where T : class, IPersistedRecord
    {
        Type type = typeof(T);
        if (type == typeof(AgentRecord)
            || type == typeof(AgentRegistryState)
            || type == typeof(PromptVersionRecord)
            || type == typeof(ReleaseRecord)
            || type == typeof(ReleaseCommandRecord))
        {
            return Registry;
        }

        if (type == typeof(DeploymentRecord))
        {
            return Deployments;
        }

        if (type == typeof(EvaluationRecord)
            || type == typeof(ShadowEvaluationRecord))
        {
            return Evaluations;
        }

        if (type == typeof(MetricSnapshotRecord))
        {
            return Metrics;
        }

        if (type == typeof(ExperimentRecord))
        {
            return Experiments;
        }

        if (type == typeof(ChatRequestMetadataRecord))
        {
            return Requests;
        }

        if (type == typeof(ShadowWorkRecord))
        {
            return ShadowWork;
        }

        throw new NotSupportedException($"No Cosmos container is defined for {type.FullName}.");
    }
}
