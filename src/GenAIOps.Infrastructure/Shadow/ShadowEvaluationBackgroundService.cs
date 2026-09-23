using GenAIOps.Application.Shadow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenAIOps.Infrastructure.Shadow;

public sealed class ShadowEvaluationBackgroundService(
    IShadowWorkQueue queue,
    IShadowEvaluationProcessor processor,
    ShadowProcessingOptions options,
    ILogger<ShadowEvaluationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ShadowWorkDelivery delivery;
            try
            {
                delivery = await queue.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            ShadowProcessingResult result;
            try
            {
                result = await processor.ProcessAsync(delivery, stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Unexpected shadow processing failure.");
                if (delivery.Attempt >= options.MaximumAttempts)
                {
                    queue.DeadLetter(delivery, "unexpected_processing_failure");
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
                    queue.Retry(delivery);
                }

                continue;
            }

            switch (result.Disposition)
            {
                case ShadowProcessingDisposition.Completed:
                case ShadowProcessingDisposition.Duplicate:
                    queue.Complete(delivery);
                    break;
                case ShadowProcessingDisposition.Retry:
                    queue.Retry(delivery);
                    break;
                case ShadowProcessingDisposition.Poisoned:
                    queue.DeadLetter(delivery, result.ErrorCode ?? "shadow_processing_failed");
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown shadow disposition '{result.Disposition}'.");
            }
        }
    }
}
