using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenAIOps.Application.Chat;
using GenAIOps.Application.Persistence;
using GenAIOps.Domain.Records;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GenAIOps.IntegrationTests;

public sealed class ChatEndpointTests
{
    [Fact]
    public async Task Chat_endpoint_returns_fake_response_contract_and_correlation()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "api-success");

        using HttpResponseMessage response =
            await client.PostAsJsonAsync("/api/chat", new { message = "Hello support" });
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("api-success", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("api-success", body.RootElement.GetProperty("correlationId").GetString());
        Assert.NotEmpty(body.RootElement.GetProperty("citations").EnumerateArray());
        Assert.NotEmpty(body.RootElement.GetProperty("toolCalls").EnumerateArray());
        Assert.True(body.RootElement.GetProperty("usage").GetProperty("totalTokens").GetInt32() > 0);

        IRepository<ChatRequestMetadataRecord> metadata =
            factory.Services.GetRequiredService<IRepository<ChatRequestMetadataRecord>>();
        RepositoryPage<ChatRequestMetadataRecord> page =
            await metadata.QueryAsync(new RecordQuery("default", "chatRequestMetadata"));
        ChatRequestMetadataRecord stored = Assert.Single(
            page.Items,
            item => item.Value.CorrelationId == "api-success").Value;
        Assert.Equal("succeeded", stored.Outcome);
    }

    [Fact]
    public async Task Chat_endpoint_returns_typed_invalid_input_problem()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.PostAsJsonAsync("/api/chat", new { message = " " });
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Chat_endpoint_returns_typed_problem_for_malformed_json()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "malformed-json");
        using StringContent content = new("{bad", System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync("/api/chat", content);
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "malformed-json",
            body.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Chat_endpoint_returns_typed_missing_production_problem()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/chat",
            new { message = "Hello", registryId = "no-production" });
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "production_agent_not_found",
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Chat_endpoint_maps_provider_failure()
    {
        await using WebApplicationFactory<Program> factory =
            CreateFactory(new FailingGateway());
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.PostAsJsonAsync("/api/chat", new { message = "Hello" });
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("provider_failure", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Chat_endpoint_maps_provider_cancellation()
    {
        await using WebApplicationFactory<Program> factory =
            CreateFactory(new CancellingGateway());
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.PostAsJsonAsync("/api/chat", new { message = "Hello" });
        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(499, (int)response.StatusCode);
        Assert.Equal("request_cancelled", body.RootElement.GetProperty("code").GetString());
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

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private sealed class FailingGateway : IChatGateway
    {
        public Task<ChatGatewayResponse> SendAsync(
            ChatGatewayRequest request,
            CancellationToken cancellationToken = default) =>
            throw new ChatProviderException("Simulated provider failure.");
    }

    private sealed class CancellingGateway : IChatGateway
    {
        public Task<ChatGatewayResponse> SendAsync(
            ChatGatewayRequest request,
            CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();
    }
}
