using GenAIOps.Application.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace GenAIOps.Api.Realtime;

public sealed class RealtimeHub : Hub<IRealtimeClient>;

public sealed class SignalRRealtimePublisher(
    IHubContext<RealtimeHub, IRealtimeClient> hub,
    ILogger<SignalRRealtimePublisher> logger) : IRealtimePublisher
{
    public Task PublishMetricsAsync(
        MetricsUpdated update,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            clients => clients.MetricsUpdated(update),
            nameof(IRealtimeClient.MetricsUpdated));

    public Task PublishEvaluationAsync(
        EvaluationUpdated update,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            clients => clients.EvaluationUpdated(update),
            nameof(IRealtimeClient.EvaluationUpdated));

    public Task PublishExperimentAsync(
        ExperimentUpdated update,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            clients => clients.ExperimentUpdated(update),
            nameof(IRealtimeClient.ExperimentUpdated));

    public Task PublishReleaseAsync(
        ReleaseUpdated update,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            clients => clients.ReleaseUpdated(update),
            nameof(IRealtimeClient.ReleaseUpdated));

    private async Task PublishAsync(
        Func<IRealtimeClient, Task> publish,
        string eventName)
    {
        try
        {
            await publish(hub.Clients.All);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Real-time event {EventName} could not be delivered.",
                eventName);
        }
    }
}
