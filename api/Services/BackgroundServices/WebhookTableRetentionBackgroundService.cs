using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.BackgroundServices;

// Trims ProcessedWebhookEvents and WebhookRetries to a 90-day window so these audit/retry
// tables don't grow unbounded. Deletes are batched to keep lock windows short enough that
// concurrent webhook writes aren't starved.
public class WebhookTableRetentionBackgroundService : BackgroundService
{
    // Runs daily. An hourly cadence would shrink the working set faster but adds churn to
    // the audit index for little real gain at the current event volume.
    private static readonly TimeSpan ProcessingInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(90);
    private const int BatchSize = 1000;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookTableRetentionBackgroundService> _logger;

    public WebhookTableRetentionBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<WebhookTableRetentionBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebhookTableRetentionBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pruning webhook tables: {ErrorMessage}", ex.Message);
            }

            try
            {
                await Task.Delay(ProcessingInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("WebhookTableRetentionBackgroundService stopped");
    }

    private async Task PruneOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

        var cutoff = DateTime.UtcNow - RetentionWindow;

        var processedDeleted = await PruneProcessedEventsAsync(dbContext, cutoff, cancellationToken);
        var retriesDeleted = await PruneTerminalRetriesAsync(dbContext, cutoff, cancellationToken);

        if (processedDeleted > 0 || retriesDeleted > 0)
        {
            _logger.LogInformation(
                "Pruned webhook tables: {ProcessedCount} processed events, {RetryCount} retries older than {Cutoff:O}",
                processedDeleted, retriesDeleted, cutoff);
        }
    }

    private static async Task<int> PruneProcessedEventsAsync(RadioWashDbContext dbContext, DateTime cutoff, CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var deleted = await dbContext.ProcessedWebhookEvents
                .Where(pwe => pwe.ProcessedAt < cutoff)
                .OrderBy(pwe => pwe.ProcessedAt)
                .Take(BatchSize)
                .ExecuteDeleteAsync(cancellationToken);

            totalDeleted += deleted;
            if (deleted < BatchSize)
            {
                break;
            }
        }
        return totalDeleted;
    }

    private static async Task<int> PruneTerminalRetriesAsync(RadioWashDbContext dbContext, DateTime cutoff, CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var deleted = await dbContext.WebhookRetries
                .Where(wr => (wr.Status == WebhookRetryStatus.Succeeded
                              || wr.Status == WebhookRetryStatus.Failed
                              || wr.Status == WebhookRetryStatus.MaxRetriesExceeded)
                              && wr.UpdatedAt < cutoff)
                .OrderBy(wr => wr.UpdatedAt)
                .Take(BatchSize)
                .ExecuteDeleteAsync(cancellationToken);

            totalDeleted += deleted;
            if (deleted < BatchSize)
            {
                break;
            }
        }
        return totalDeleted;
    }
}
