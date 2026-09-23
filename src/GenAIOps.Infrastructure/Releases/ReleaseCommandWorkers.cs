using GenAIOps.Application.Releases;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenAIOps.Infrastructure.Releases;

public sealed record PromotionWorkerOptions(
    bool Enabled,
    string RegistryId,
    string Version,
    string Actor,
    string IdempotencyKey,
    string ExpectedETag);

public sealed record RollbackWorkerOptions(
    bool Enabled,
    string RegistryId,
    string Actor,
    string IdempotencyKey,
    string ExpectedETag);

public sealed class PromotionCommandWorker(
    IReleaseWorkflowService workflows,
    PromotionWorkerOptions options,
    ILogger<PromotionCommandWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        await workflows.PromoteAsync(
            options.Version,
            new ReleaseCommand(
                options.RegistryId,
                options.Actor,
                options.IdempotencyKey,
                options.ExpectedETag),
            stoppingToken);
        logger.LogInformation("Promotion completed for {Version}.", options.Version);
    }
}

public sealed class RollbackCommandWorker(
    IReleaseWorkflowService workflows,
    RollbackWorkerOptions options,
    ILogger<RollbackCommandWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        ReleaseWorkflowResult result = await workflows.RollbackAsync(
            new ReleaseCommand(
                options.RegistryId,
                options.Actor,
                options.IdempotencyKey,
                options.ExpectedETag),
            stoppingToken);
        logger.LogInformation(
            "Rollback completed to {Version}.",
            result.Release.PromptVersion);
    }
}
