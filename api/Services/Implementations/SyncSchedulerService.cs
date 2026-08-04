using Hangfire;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class SyncSchedulerService : ISyncSchedulerService
{
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<SyncSchedulerService> _logger;

    public SyncSchedulerService(
        IRecurringJobManager recurringJobManager,
        IBackgroundJobClient backgroundJobClient,
        IUnitOfWork unitOfWork,
        ISubscriptionService subscriptionService,
        ILogger<SyncSchedulerService> logger)
    {
        _recurringJobManager = recurringJobManager;
        _backgroundJobClient = backgroundJobClient;
        _unitOfWork = unitOfWork;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public void InitializeScheduledJobs()
    {
        _logger.LogInformation("Initializing scheduled sync jobs");

        // Schedule main sync job to run daily at 00:01 (12:01 AM)
        TryScheduleRecurringJob(
            "playlist-sync-processor",
            () => _recurringJobManager.AddOrUpdate(
                "playlist-sync-processor",
                () => ProcessScheduledSyncsAsync(),
                "1 0 * * *" // Daily at 00:01
            ));

        // Schedule subscription validation job daily at 02:00
        TryScheduleRecurringJob(
            "subscription-validator",
            () => _recurringJobManager.AddOrUpdate(
                "subscription-validator",
                () => ValidateSubscriptionsAsync(),
                "0 2 * * *" // Daily at 2 AM
            ));

        _logger.LogInformation("Scheduled sync jobs initialized");
    }

    /// <summary>
    /// Registration runs during startup, is idempotent, and the job definitions persist in
    /// Hangfire storage across restarts — so a failure here must never take the API down.
    /// The known offender: an API instance killed while holding Hangfire's distributed lock
    /// for a recurring job leaves the lock row orphaned, and until Hangfire's staleness
    /// cutoff (DistributedLockTimeout, 10 minutes) passes, every AddOrUpdate for that job
    /// times out. Previously that surfaced as an unhandled exception in Program and a crash
    /// loop. Already-registered jobs keep firing on their stored schedule; the next restart
    /// retries registration.
    /// </summary>
    private void TryScheduleRecurringJob(string jobId, Action register)
    {
        try
        {
            register();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to register recurring job {JobId}; continuing startup. A previously " +
                "stored schedule for this job keeps firing, and registration is retried on the " +
                "next restart", jobId);
        }
    }

    public async Task ProcessScheduledSyncsAsync()
    {
        _logger.LogInformation("Starting scheduled sync processing");

        var dueConfigs = await _unitOfWork.SyncConfigs.GetDueForSyncAsync(DateTime.UtcNow);

        _logger.LogInformation("Found {ConfigCount} sync configurations due for processing", dueConfigs.Count());

        foreach (var config in dueConfigs)
        {
            try
            {
                // Check if user has active subscription
                var hasActiveSubscription = await _subscriptionService.HasActiveSubscriptionAsync(config.UserId);

                if (!hasActiveSubscription)
                {
                    _logger.LogWarning("User {UserId} does not have active subscription, disabling sync config {ConfigId}",
                        config.UserId, config.Id);
                    await _unitOfWork.SyncConfigs.DisableConfigAsync(config.Id, AutoDisableReason.SubscriptionInactive);
                    continue;
                }

                // Queue individual sync job - pass only config ID to avoid circular reference
                _backgroundJobClient.Enqueue<IPlaylistSyncService>(service => service.SyncPlaylistAsync(config.Id));

                _logger.LogDebug("Queued sync job for config {ConfigId}, user {UserId}", config.Id, config.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing sync config {ConfigId}", config.Id);
            }
        }

        _logger.LogInformation("Scheduled sync processing completed");
    }

    public async Task ValidateSubscriptionsAsync()
    {
        _logger.LogInformation("Starting subscription validation");
        await _subscriptionService.ValidateSubscriptionsAsync();
        _logger.LogInformation("Subscription validation completed");
    }

}
