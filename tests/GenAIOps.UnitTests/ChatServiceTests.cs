using System.Text.Json;
using GenAIOps.Application.Chat;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Domain.Records;
using GenAIOps.Infrastructure.Chat;
using GenAIOps.Infrastructure.Persistence;

namespace GenAIOps.UnitTests;

public sealed class ChatServiceTests
{
    [Fact]
    public async Task Fake_gateway_is_deterministic_and_returns_complete_contract()
    {
        FakeChatGateway gateway = new();
        ChatGatewayRequest request = new("support", "v1", "Reset my account", "correlation-1");

        ChatGatewayResponse first = await gateway.SendAsync(request);
        ChatGatewayResponse second = await gateway.SendAsync(request);

        Assert.Equal(first.Message, second.Message);
        Assert.Equal(first.ProviderResponseId, second.ProviderResponseId);
        Assert.Equal(first.Citations, second.Citations);
        Assert.Equal(first.ToolCalls, second.ToolCalls);
        Assert.Equal(first.Usage, second.Usage);
        Assert.Contains("support/v1", first.Message, StringComparison.Ordinal);
        Assert.NotEmpty(first.Citations);
        Assert.NotEmpty(first.ToolCalls);
        Assert.NotNull(first.Usage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Chat_service_rejects_invalid_input(string message)
    {
        ChatService service = await CreateServiceAsync(new FakeChatGateway());

        ChatValidationException exception = await Assert.ThrowsAsync<ChatValidationException>(
            () => service.SendAsync("default", message, "correlation-2"));

        Assert.Equal("invalid_request", exception.Code);
    }

    [Fact]
    public async Task Chat_service_reports_missing_production()
    {
        ChatService service = CreateServiceWithoutProduction(new FakeChatGateway());

        ProductionAgentNotFoundException exception =
            await Assert.ThrowsAsync<ProductionAgentNotFoundException>(
                () => service.SendAsync("missing", "Hello", "correlation-3"));

        Assert.Equal("production_agent_not_found", exception.Code);
    }

    [Fact]
    public async Task Provider_failure_is_typed_and_persists_only_safe_metadata()
    {
        InMemoryRepository<ChatRequestMetadataRecord> metadata = new();
        ChatService service = await CreateServiceAsync(
            new DelegateGateway(
                (_, _) => throw new ChatProviderException("Provider unavailable.")),
            metadata);
        const string secretInput = "private-user-content-should-not-be-stored";

        ChatProviderException exception = await Assert.ThrowsAsync<ChatProviderException>(
            () => service.SendAsync("default", secretInput, "correlation-4"));
        ChatRequestMetadataRecord stored = await FindMetadataAsync(
            metadata,
            "correlation-4");

        Assert.Equal("provider_failure", exception.Code);
        Assert.Equal("failed", stored.Outcome);
        Assert.Equal(secretInput.Length, stored.InputCharacterCount);
        Assert.DoesNotContain(
            secretInput,
            JsonSerializer.Serialize(stored),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_is_typed_and_recorded()
    {
        InMemoryRepository<ChatRequestMetadataRecord> metadata = new();
        ChatService service = await CreateServiceAsync(
            new DelegateGateway((_, _) => throw new OperationCanceledException()),
            metadata);

        ChatRequestCanceledException exception =
            await Assert.ThrowsAsync<ChatRequestCanceledException>(
                () => service.SendAsync("default", "Cancel me", "correlation-5"));
        ChatRequestMetadataRecord stored = await FindMetadataAsync(
            metadata,
            "correlation-5");

        Assert.Equal("request_cancelled", exception.Code);
        Assert.Equal("cancelled", stored.Outcome);
    }

    [Fact]
    public async Task Cancellation_during_registry_resolution_is_typed()
    {
        ChatService service = CreateServiceWithoutProduction(new FakeChatGateway());
        using CancellationTokenSource source = new();
        source.Cancel();

        ChatRequestCanceledException exception =
            await Assert.ThrowsAsync<ChatRequestCanceledException>(
                () => service.SendAsync(
                    "default",
                    "Cancel me",
                    "correlation-registry-cancel",
                    source.Token));

        Assert.Equal("request_cancelled", exception.Code);
    }

    [Fact]
    public async Task Metadata_failure_is_not_misreported_as_provider_failure()
    {
        AgentRegistryService registry =
            new(new InMemoryRepository<GenAIOps.Domain.Registry.AgentRegistryState>());
        RegistrySnapshot candidate =
            await registry.RegisterCandidateAsync("default", "foundry-support", "v1");
        await registry.PromoteCandidateAsync("default", "foundry-support", candidate.ETag);
        ChatService service = new(registry, new FakeChatGateway(), new FailingMetadataRepository());

        ChatMetadataPersistenceException exception =
            await Assert.ThrowsAsync<ChatMetadataPersistenceException>(
                () => service.SendAsync("default", "Hello", "correlation-metadata-fail"));

        Assert.Equal("metadata_persistence_failure", exception.Code);
    }

    [Fact]
    public async Task Success_propagates_correlation_and_records_provider_identity()
    {
        InMemoryRepository<ChatRequestMetadataRecord> metadata = new();
        ChatService service = await CreateServiceAsync(new FakeChatGateway(), metadata);

        ChatResponse response =
            await service.SendAsync("default", "Hello", "correlation-6");
        ChatRequestMetadataRecord stored = await FindMetadataAsync(
            metadata,
            "correlation-6");

        Assert.Equal("correlation-6", response.CorrelationId);
        Assert.Equal(response.ProviderResponseId, stored.ProviderResponseId);
        Assert.Equal("foundry-support", stored.AgentId);
        Assert.Equal("v1", stored.PromptVersion);
        Assert.Equal("succeeded", stored.Outcome);
    }

    private static ChatService CreateServiceWithoutProduction(IChatGateway gateway) =>
        new(
            new AgentRegistryService(new InMemoryRepository<GenAIOps.Domain.Registry.AgentRegistryState>()),
            gateway,
            new InMemoryRepository<ChatRequestMetadataRecord>());

    private static async Task<ChatService> CreateServiceAsync(
        IChatGateway gateway,
        InMemoryRepository<ChatRequestMetadataRecord>? metadata = null)
    {
        AgentRegistryService registry =
            new(new InMemoryRepository<GenAIOps.Domain.Registry.AgentRegistryState>());
        RegistrySnapshot candidate =
            await registry.RegisterCandidateAsync("default", "foundry-support", "v1");
        await registry.PromoteCandidateAsync("default", "foundry-support", candidate.ETag);
        return new ChatService(
            registry,
            gateway,
            metadata ?? new InMemoryRepository<ChatRequestMetadataRecord>());
    }

    private static async Task<ChatRequestMetadataRecord> FindMetadataAsync(
        InMemoryRepository<ChatRequestMetadataRecord> metadata,
        string correlationId)
    {
        RepositoryPage<ChatRequestMetadataRecord> page =
            await metadata.QueryAsync(new RecordQuery("default", "chatRequestMetadata"));
        return Assert.Single(
            page.Items,
            item => item.Value.CorrelationId == correlationId).Value;
    }

    private sealed class DelegateGateway(
        Func<ChatGatewayRequest, CancellationToken, Task<ChatGatewayResponse>> handler)
        : IChatGateway
    {
        public Task<ChatGatewayResponse> SendAsync(
            ChatGatewayRequest request,
            CancellationToken cancellationToken = default) =>
            handler(request, cancellationToken);
    }

    private sealed class FailingMetadataRepository : IRepository<ChatRequestMetadataRecord>
    {
        public Task<StoredItem<ChatRequestMetadataRecord>?> GetAsync(
            string id,
            string partitionKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredItem<ChatRequestMetadataRecord>?>(null);

        public Task<StoredItem<ChatRequestMetadataRecord>> CreateAsync(
            ChatRequestMetadataRecord item,
            CancellationToken cancellationToken = default) =>
            throw new PersistenceException("Simulated metadata failure.");

        public Task<StoredItem<ChatRequestMetadataRecord>> ReplaceAsync(
            ChatRequestMetadataRecord item,
            string expectedETag,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RepositoryPage<ChatRequestMetadataRecord>> QueryAsync(
            RecordQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
