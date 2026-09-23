using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenAIOps.Application.Chat;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GenAIOps.IntegrationTests;

public sealed class EvaluationsEndpointTests
{
    [Fact]
    public async Task Successful_chat_is_shadowed_and_queryable_without_candidate_exposure()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "evaluation-endpoint-success");

        using HttpResponseMessage chat =
            await client.PostAsJsonAsync("/api/chat", new { message = "Reset account" });
        using JsonDocument chatBody = await ReadJsonAsync(chat);
        JsonElement evaluation = await WaitForEvaluationAsync(
            client,
            "evaluation-endpoint-success");

        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        Assert.Contains(
            "local-support-agent/v1",
            chatBody.RootElement.GetProperty("message").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "local-support-candidate",
            chatBody.RootElement.GetProperty("message").GetString(),
            StringComparison.Ordinal);
        Assert.Equal("Completed", evaluation.GetProperty("lifecycle").GetString());
        Assert.Equal(
            "local-support-candidate",
            evaluation.GetProperty("candidateAgentId").GetString());
        Assert.Equal(3, evaluation.GetProperty("scores").EnumerateObject().Count());
    }

    [Fact]
    public async Task Candidate_latency_does_not_delay_visible_production_response()
    {
        await using WebApplicationFactory<Program> factory =
            CreateFactory(new ConditionalGateway(candidateDelay: TimeSpan.FromSeconds(2)));
        using HttpClient client = factory.CreateClient();
        Stopwatch stopwatch = Stopwatch.StartNew();

        using HttpResponseMessage chat =
            await client.PostAsJsonAsync("/api/chat", new { message = "Fast production" });
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Production response took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Candidate_failure_does_not_change_production_response_and_is_queryable()
    {
        await using WebApplicationFactory<Program> factory =
            CreateFactory(new ConditionalGateway(failCandidate: true));
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "evaluation-endpoint-failure");

        using HttpResponseMessage chat =
            await client.PostAsJsonAsync("/api/chat", new { message = "Visible response" });
        JsonElement evaluation = await WaitForEvaluationAsync(
            client,
            "evaluation-endpoint-failure");

        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        Assert.Equal("Poisoned", evaluation.GetProperty("lifecycle").GetString());
        Assert.Equal(
            "shadow_provider_failure",
            evaluation.GetProperty("errorCode").GetString());
        Assert.Equal(3, evaluation.GetProperty("attemptCount").GetInt32());
    }

    [Fact]
    public async Task Evaluations_endpoint_rejects_invalid_page_size()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/api/evaluations?pageSize=1000");
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid request", body.RootElement.GetProperty("title").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(IChatGateway? gateway = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder =>
            {
                builder.UseEnvironment("Development");
                if (gateway is not null)
                {
                    builder.ConfigureTestServices(
                        services =>
                        {
                            services.RemoveAll<IChatGateway>();
                            services.AddSingleton(gateway);
                        });
                }
            });

    private static async Task<JsonElement> WaitForEvaluationAsync(
        HttpClient client,
        string correlationId)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            using HttpResponseMessage response =
                await client.GetAsync("/api/evaluations");
            using JsonDocument body = await ReadJsonAsync(response);
            JsonElement? match = body.RootElement.GetProperty("evaluations")
                .EnumerateArray()
                .FirstOrDefault(
                    item => item.GetProperty("correlationId").GetString() == correlationId);
            if (match is { ValueKind: not JsonValueKind.Undefined })
            {
                return match.Value.Clone();
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Evaluation '{correlationId}' was not persisted.");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private sealed class ConditionalGateway(
        TimeSpan? candidateDelay = null,
        bool failCandidate = false) : IChatGateway
    {
        public async Task<ChatGatewayResponse> SendAsync(
            ChatGatewayRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.AgentName.Contains("candidate", StringComparison.Ordinal))
            {
                if (candidateDelay is { } delay)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                if (failCandidate)
                {
                    throw new ChatProviderException("Candidate unavailable.");
                }
            }

            return await new GenAIOps.Infrastructure.Chat.FakeChatGateway()
                .SendAsync(request, cancellationToken);
        }
    }
}
