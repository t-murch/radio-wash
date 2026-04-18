using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.BackgroundServices;

public class SubscriptionExpiryBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubscriptionExpiryBackgroundService> _logger;
    private readonly TimeSpan _processingInterval = TimeSpan.FromHours(1);

    public SubscriptionExpiryBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<SubscriptionExpiryBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SubscriptionExpiryBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ValidateOnceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating subscription expiry: {ErrorMessage}", ex.Message);
            }

            try
            {
                await Task.Delay(_processingInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Graceful shutdown
                break;
            }
        }

        _logger.LogInformation("SubscriptionExpiryBackgroundService stopped");
    }

    private async Task ValidateOnceAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
        await subscriptionService.ValidateSubscriptionsAsync();
    }
}
