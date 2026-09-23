using System.Net;
using Azure.Core;
using GenAIOps.Application.Chat;
using GenAIOps.Application.Persistence;
using GenAIOps.Application.Registry;
using GenAIOps.Application.Shadow;
using GenAIOps.Domain.Records;
using GenAIOps.Domain.Registry;
using GenAIOps.Infrastructure.Chat;
using GenAIOps.Infrastructure.Persistence;
using GenAIOps.Infrastructure.Shadow;

namespace GenAIOps.UnitTests;

public sealed class ShadowEvaluationTests
{
    [Fact]
    public async Task Successful_processing_persists_hidden_candidate_comparison_and_scores()
    {
        InMemoryRepository<ShadowEvaluationRecord> repository = new();
        ShadowEvaluationProcessor processor = CreateProcessor(
            new FakeChatGateway(),
            new FakeResponseEvaluator(),
            repository);

        ShadowProcessingResult result = await processor.ProcessAsync(Delivery());
        StoredItem<ShadowEvaluationRecord>? stored =
            await repository.GetAsync("shadow-1", "default");

        Assert.Equal(ShadowProcessingDisposition.Completed, result.Disposition);
        Assert.NotNull(stored);
        Assert.Equal(EvaluationLifecycle.Completed, stored.Value.Lifecycle);
        Assert.Equal("Visible production output", stored.Value.ProductionOutput);
        Assert.Contains("candidate/v2", stored.Value.CandidateOutput, StringComparison.Ordinal);
        Assert.Equal(3, stored.Value.Scores.Count);
        Assert.Contains("taskAdherence", stored.Value.Scores.Keys);
        Assert.Contains("groundedness", stored.Value.Scores.Keys);
        Assert.Contains("toolAccuracy", stored.Value.Scores.Keys);
    }

    [Fact]
    public async Task Duplicate_delivery_does_not_invoke_candidate_twice()
    {
        CountingGateway gateway = new();
        InMemoryRepository<ShadowEvaluationRecord> repository = new();
        ShadowEvaluationProcessor processor =
            CreateProcessor(gateway, new FakeResponseEvaluator(), repository);

        ShadowProcessingResult first = await processor.ProcessAsync(Delivery());
        ShadowProcessingResult duplicate = await processor.ProcessAsync(Delivery());

        Assert.Equal(ShadowProcessingDisposition.Completed, first.Disposition);
        Assert.Equal(ShadowProcessingDisposition.Duplicate, duplicate.Disposition);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public async Task Concurrent_duplicate_deliveries_claim_once_before_candidate_invocation()
    {
        CountingGateway gateway = new();
        InMemoryRepository<ShadowEvaluationRecord> repository = new();
        ShadowEvaluationProcessor processor =
            CreateProcessor(gateway, new FakeResponseEvaluator(), repository);

        ShadowProcessingResult[] results = await Task.WhenAll(
            processor.ProcessAsync(Delivery()),
            processor.ProcessAsync(Delivery()));

        Assert.Single(
            results,
            result => result.Disposition == ShadowProcessingDisposition.Completed);
        Assert.Single(
            results,
            result => result.Disposition == ShadowProcessingDisposition.Duplicate);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public async Task Transient_candidate_failure_persists_retryable_claim()
    {
        InMemoryRepository<ShadowEvaluationRecord> repository = new();
        ShadowEvaluationProcessor processor = CreateProcessor(
            new ThrowingGateway(new ChatProviderException("Unavailable")),
            new FakeResponseEvaluator(),
            repository);

        ShadowProcessingResult result = await processor.ProcessAsync(Delivery(attempt: 1));

        Assert.Equal(ShadowProcessingDisposition.Retry, result.Disposition);
        Assert.Equal("shadow_provider_failure", result.ErrorCode);
        StoredItem<ShadowEvaluationRecord>? stored =
            await repository.GetAsync("shadow-1", "default");
        Assert.Equal(EvaluationLifecycle.Pending, stored?.Value.Lifecycle);
        Assert.Equal(1, stored?.Value.AttemptCount);
    }

    [Fact]
    public async Task Exhausted_candidate_failure_is_persisted_and_poisoned()
    {
        InMemoryRepository<ShadowEvaluationRecord> repository = new();
        ShadowEvaluationProcessor processor = CreateProcessor(
            new ThrowingGateway(new ChatProviderException("Unavailable")),
            new FakeResponseEvaluator(),
            repository);

        ShadowProcessingResult result = await processor.ProcessAsync(Delivery(attempt: 3));
        StoredItem<ShadowEvaluationRecord>? stored =
            await repository.GetAsync("shadow-1", "default");

        Assert.Equal(ShadowProcessingDisposition.Poisoned, result.Disposition);
        Assert.Equal(EvaluationLifecycle.Poisoned, stored?.Value.Lifecycle);
        Assert.Equal("shadow_provider_failure", stored?.Value.ErrorCode);
        Assert.Equal(3, stored?.Value.AttemptCount);
    }

    [Fact]
    public async Task Candidate_timeout_is_retryable_and_cancellation_is_propagated()
    {
        InMemoryRepository<ShadowEvaluationRecord> repository = new();
        ShadowEvaluationProcessor processor = CreateProcessor(
            new WaitingGateway(),
            new FakeResponseEvaluator(),
            repository,
            TimeSpan.FromMilliseconds(20));

        ShadowProcessingResult result = await processor.ProcessAsync(Delivery());

        Assert.Equal(ShadowProcessingDisposition.Retry, result.Disposition);
        Assert.Equal("shadow_timeout", result.ErrorCode);
    }

    [Fact]
    public async Task Evaluation_failure_persists_candidate_output_when_attempts_are_exhausted()
    {
        InMemoryRepository<ShadowEvaluationRecord> repository = new();
        ShadowEvaluationProcessor processor = CreateProcessor(
            new FakeChatGateway(),
            new ThrowingEvaluator(),
            repository);

        ShadowProcessingResult result = await processor.ProcessAsync(Delivery(attempt: 3));
        StoredItem<ShadowEvaluationRecord>? stored =
            await repository.GetAsync("shadow-1", "default");

        Assert.Equal(ShadowProcessingDisposition.Poisoned, result.Disposition);
        Assert.Equal(EvaluationLifecycle.Failed, stored?.Value.Lifecycle);
        Assert.Equal("evaluation_failure", stored?.Value.ErrorCode);
        Assert.NotNull(stored?.Value.CandidateOutput);
    }

    [Fact]
    public async Task Chat_success_publishes_sanitized_work_without_exposing_candidate()
    {
        InMemoryRepository<AgentRegistryState> registryRepository = new();
        AgentRegistryService registry = new(registryRepository);
        RegistrySnapshot initial =
            await registry.RegisterCandidateAsync("default", "production", "v1");
        RegistrySnapshot promoted =
            await registry.PromoteCandidateAsync("default", "production", initial.ETag);
        await registry.RegisterCandidateAsync("default", "candidate", "v2", promoted.ETag);
        CapturingPublisher publisher = new();
        ChatService service = new(
            registry,
            new FakeChatGateway(),
            new InMemoryRepository<ChatRequestMetadataRecord>(),
            publisher);

        ChatResponse response =
            await service.SendAsync("default", "Customer request", "shadow-publish");

        Assert.Contains("production/v1", response.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(publisher.Published);
        Assert.Equal("candidate", publisher.Published.CandidateAgentId);
        Assert.Equal(response.Message, publisher.Published.ProductionOutput);
        Assert.DoesNotContain(
            "local_lookup",
            System.Text.Json.JsonSerializer.Serialize(publisher.Published),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shadow_publication_failure_does_not_change_production_response()
    {
        InMemoryRepository<AgentRegistryState> registryRepository = new();
        AgentRegistryService registry = new(registryRepository);
        RegistrySnapshot initial =
            await registry.RegisterCandidateAsync("default", "production", "v1");
        RegistrySnapshot promoted =
            await registry.PromoteCandidateAsync("default", "production", initial.ETag);
        await registry.RegisterCandidateAsync("default", "candidate", "v2", promoted.ETag);
        ChatService service = new(
            registry,
            new FakeChatGateway(),
            new InMemoryRepository<ChatRequestMetadataRecord>(),
            new ThrowingPublisher());

        ChatResponse response =
            await service.SendAsync("default", "Customer request", "shadow-publish-failure");

        Assert.Contains("production/v1", response.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Queue_retry_increments_attempt_and_dead_letter_retains_reason()
    {
        InMemoryShadowWorkQueue queue = new();
        queue.TryPublish(Delivery().Work);
        ShadowWorkDelivery first = await queue.ReceiveAsync();

        queue.Retry(first);
        ShadowWorkDelivery retry = await queue.ReceiveAsync();
        queue.DeadLetter(retry, "poison");

        Assert.Equal(2, retry.Attempt);
        Assert.Equal("poison", Assert.Single(queue.DeadLetters).Reason);
    }

    [Fact]
    public async Task Persistent_queue_claims_retries_and_completes_shared_work()
    {
        InMemoryRepository<ShadowWorkRecord> repository = new();
        PersistentShadowWorkQueue publisher = new(repository);
        PersistentShadowWorkQueue worker = new(repository);
        Assert.True(publisher.TryPublish(Delivery().Work));

        ShadowWorkDelivery first = await worker.ReceiveAsync();
        worker.Retry(first);
        ShadowWorkDelivery retry = await worker.ReceiveAsync();
        worker.Complete(retry);
        StoredItem<ShadowWorkRecord>? stored =
            await repository.GetAsync("shadow-1", "shadow-work");

        Assert.Equal(2, retry.Attempt);
        Assert.Equal(ShadowWorkLifecycle.Completed, stored?.Value.Lifecycle);
    }

    [Fact]
    public async Task Foundry_evaluation_adapter_authenticates_and_maps_named_scores()
    {
        CapturingHandler handler = new(
            """{"scores":{"taskAdherence":0.9,"groundedness":0.8,"toolAccuracy":1.0}}""");
        FoundryEvaluationClient client = new(
            new HttpClient(handler),
            new FoundryEvaluationOptions(new Uri("https://foundry.example/evaluations")),
            new StaticTokenCredential());
        ChatGatewayResponse candidate = await new FakeChatGateway().SendAsync(
            new ChatGatewayRequest("candidate", "v2", "input", "foundry-evaluation"));

        EvaluationScores scores = await client.EvaluateAsync(
            new EvaluationInput("input", "production", candidate));

        Assert.Equal(0.9, scores.TaskAdherence);
        Assert.Equal(0.8, scores.Groundedness);
        Assert.Equal(1, scores.ToolAccuracy);
        Assert.Equal("Bearer test-token", handler.Authorization);
        Assert.Contains("builtin.task_adherence", handler.Body, StringComparison.Ordinal);
        Assert.Contains("builtin.groundedness", handler.Body, StringComparison.Ordinal);
        Assert.Contains("builtin.tool_call_accuracy", handler.Body, StringComparison.Ordinal);
    }

    private static ShadowEvaluationProcessor CreateProcessor(
        IChatGateway gateway,
        IResponseEvaluator evaluator,
        InMemoryRepository<ShadowEvaluationRecord> repository,
        TimeSpan? timeout = null) =>
        new(
            gateway,
            evaluator,
            repository,
            new ShadowProcessingOptions(timeout ?? TimeSpan.FromSeconds(2), MaximumAttempts: 3));

    private static ShadowWorkDelivery Delivery(int attempt = 1) =>
        new(
            new ShadowWorkItem(
                "shadow-1",
                "default",
                "production",
                "v1",
                "candidate",
                "v2",
                "Customer request",
                "Visible production output",
                DateTimeOffset.Parse("2026-09-23T10:00:00Z")),
            attempt);

    private sealed class CountingGateway : IChatGateway
    {
        public int CallCount { get; private set; }

        public Task<ChatGatewayResponse> SendAsync(
            ChatGatewayRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return new FakeChatGateway().SendAsync(request, cancellationToken);
        }
    }

    private sealed class ThrowingGateway(Exception exception) : IChatGateway
    {
        public Task<ChatGatewayResponse> SendAsync(
            ChatGatewayRequest request,
            CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class WaitingGateway : IChatGateway
    {
        public async Task<ChatGatewayResponse> SendAsync(
            ChatGatewayRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class ThrowingEvaluator : IResponseEvaluator
    {
        public Task<EvaluationScores> EvaluateAsync(
            EvaluationInput input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Judge unavailable.");
    }

    private sealed class CapturingPublisher : IShadowWorkPublisher
    {
        public ShadowWorkItem? Published { get; private set; }

        public bool TryPublish(ShadowWorkItem work)
        {
            Published = work;
            return true;
        }
    }

    private sealed class ThrowingPublisher : IShadowWorkPublisher
    {
        public bool TryPublish(ShadowWorkItem work) =>
            throw new InvalidOperationException("Queue unavailable.");
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AccessToken("test-token", DateTimeOffset.MaxValue));
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseJson,
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
