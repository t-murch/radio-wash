using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Background;

public class WebhookRetryBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookRetryBackgroundService> _logger;
    private readonly TimeSpan _processingInterval = TimeSpan.FromMinutes(1); // Check for retries every minute

    public WebhookRetryBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<WebhookRetryBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook retry background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingRetries();
                await Task.Delay(_processingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in webhook retry background service: {ErrorMessage}", ex.Message);
                
                // Wait a bit longer on error to avoid tight loop
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Webhook retry background service stopped");
    }

    private async Task ProcessPendingRetries()
    {
        using var scope = _serviceProvider.CreateScope();
        var webhookRetryService = scope.ServiceProvider.GetRequiredService<IWebhookRetryService>();
        
        await webhookRetryService.ProcessPendingRetriesAsync();
    }
}