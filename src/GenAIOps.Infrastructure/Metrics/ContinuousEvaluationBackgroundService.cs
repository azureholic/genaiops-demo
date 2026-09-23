using GenAIOps.Application.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenAIOps.Infrastructure.Metrics;

public sealed class ContinuousEvaluationBackgroundService(
    IContinuousEvaluationRunner runner,
    IMetricsAggregator aggregator,
    ContinuousEvaluationOptions options,
    ILogger<ContinuousEvaluationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        options.Validate();
        await RunOnceAsync(stoppingToken);
        using PeriodicTimer timer = new(options.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            long scheduleTicks = options.Interval.Ticks;
            long scheduledTicks = now.UtcTicks - now.UtcTicks % scheduleTicks;
            DateTimeOffset scheduledAt = new(scheduledTicks, TimeSpan.Zero);
            await runner.RunAsync(options.RegistryId, scheduledAt, cancellationToken);
            long bucketTicks = options.AggregationWindow.Ticks;
            long startTicks = now.UtcTicks - now.UtcTicks % bucketTicks;
            DateTimeOffset start = new(startTicks, TimeSpan.Zero);
            DateTimeOffset end = start + options.AggregationWindow;
            await aggregator.AggregateAsync(
                new AggregationWindow(options.RegistryId, start, end),
                cancellationToken);
            await aggregator.AggregateAsync(
                new AggregationWindow(
                    options.RegistryId,
                    start - options.AggregationWindow,
                    start),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled continuous evaluation failed.");
        }
    }
}
