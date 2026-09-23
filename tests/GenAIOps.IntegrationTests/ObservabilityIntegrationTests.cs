using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using GenAIOps.Application.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenAIOps.IntegrationTests;

[Collection(nameof(ObservabilityIntegrationCollection))]
public sealed class ObservabilityIntegrationTests
{
    [Fact]
    public async Task Api_and_chat_activities_honor_incoming_w3c_context_without_sensitive_tags()
    {
        ConcurrentBag<Activity> stopped = [];
        using ActivityListener listener = new()
        {
            ShouldListenTo = source =>
                source.Name == GenAIOpsTelemetry.ActivitySourceName
                || source.Name == "Microsoft.AspNetCore",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>().WithWebHostBuilder(
                builder => builder.UseEnvironment("Development"));
        using HttpClient client = factory.CreateClient();
        const string traceId = "11111111111111111111111111111111";
        const string sensitiveCorrelation = "secret-correlation-header";
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "traceparent",
            $"00-{traceId}-2222222222222222-01");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", sensitiveCorrelation);

        using HttpResponseMessage response =
            await client.PostAsJsonAsync("/api/chat", new { message = "secret prompt body" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Activity chat = Assert.Single(
            stopped,
            activity => activity.OperationName == "chat.send");
        Assert.Equal(traceId, chat.TraceId.ToHexString());
        Assert.All(
            chat.TagObjects,
            tag => Assert.Contains(tag.Key, GenAIOpsTelemetry.AllowedTags));
        string serializedTags = string.Join(
            '|',
            chat.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));
        Assert.DoesNotContain(sensitiveCorrelation, serializedTags, StringComparison.Ordinal);
        Assert.DoesNotContain("secret prompt body", serializedTags, StringComparison.Ordinal);
        Assert.Equal(
            "success",
            chat.TagObjects
                .Single(tag => tag.Key == GenAIOpsTelemetry.OutcomeTag)
                .Value);
    }
}

[CollectionDefinition(
    nameof(ObservabilityIntegrationCollection),
    DisableParallelization = true)]
public sealed class ObservabilityIntegrationCollection;
