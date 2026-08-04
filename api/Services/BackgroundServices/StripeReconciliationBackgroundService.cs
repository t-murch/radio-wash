using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.BackgroundServices;

public class StripeReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StripeReconciliationBackgroundService> _logger;
    private readonly TimeSpan _processingInterval;

    public StripeReconciliationBackgroundService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<StripeReconciliationBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _processingInterval = TimeSpan.FromMinutes(
            configuration.GetValue("Stripe:ReconciliationIntervalMinutes", 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StripeReconciliationBackgroundService started (interval {Interval})", _processingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Interval first: at boot, webhooks are healthier signals than a reconcile
            // sweep, and this avoids hammering Stripe on crash-restart loops.
            try
            {
                await Task.Delay(_processingInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Stripe reconciliation: {ErrorMessage}", ex.Message);
            }
        }

        _logger.LogInformation("StripeReconciliationBackgroundService stopped");
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var reconciliationService = scope.ServiceProvider.GetRequiredService<IStripeReconciliationService>();
        await reconciliationService.ReconcileAsync(cancellationToken);
    }
}
