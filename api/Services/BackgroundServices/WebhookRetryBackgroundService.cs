using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.BackgroundServices;

public class WebhookRetryBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookRetryBackgroundService> _logger;
    private readonly TimeSpan _processingInterval = TimeSpan.FromMinutes(1);

    public WebhookRetryBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<WebhookRetryBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebhookRetryBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingRetriesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook retries: {ErrorMessage}", ex.Message);
            }

            // Wait for the next processing interval
            await Task.Delay(_processingInterval, stoppingToken);
        }

        _logger.LogInformation("WebhookRetryBackgroundService stopped");
    }

    private async Task ProcessPendingRetriesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var webhookRetryService = scope.ServiceProvider.GetRequiredService<IWebhookRetryService>();

        try
        {
            var pendingRetries = await webhookRetryService.GetPendingRetriesAsync();
            var retryCount = pendingRetries.Count();

            if (retryCount > 0)
            {
                _logger.LogInformation("Processing {RetryCount} pending webhook retries", retryCount);

                foreach (var retry in pendingRetries)
                {
                    try
                    {
                        await webhookRetryService.ProcessRetryAsync(retry);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process webhook retry for event {EventId}: {ErrorMessage}", 
                            retry.EventId, ex.Message);
                        // Individual retry failures are handled by the retry service
                        // Continue processing other retries
                    }
                }

                _logger.LogInformation("Completed processing {RetryCount} webhook retries", retryCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pending webhook retries: {ErrorMessage}", ex.Message);
        }
    }
}