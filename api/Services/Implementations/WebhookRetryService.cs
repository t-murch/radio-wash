using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Services.Implementations;

public class WebhookRetryService : IWebhookRetryService
{
    private readonly RadioWashDbContext _dbContext;
    private readonly IWebhookProcessor _webhookProcessor;
    private readonly ILogger<WebhookRetryService> _logger;

    // Exponential backoff configuration
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromHours(24);
    private const int MaxRetries = 3;
    private const double BackoffMultiplier = 2.0;

    // Retryable error patterns
    private static readonly HashSet<string> RetryableStripeErrorCodes = new()
    {
        "rate_limit",
        "api_connection_error", 
        "api_error"
    };

    public WebhookRetryService(
        RadioWashDbContext dbContext,
        IWebhookProcessor webhookProcessor,
        ILogger<WebhookRetryService> logger)
    {
        _dbContext = dbContext;
        _webhookProcessor = webhookProcessor;
        _logger = logger;
    }

    public async Task ScheduleRetryAsync(string eventId, string eventType, string payload, 
        string signature, string errorMessage, int attemptNumber)
    {
        try
        {
            if (attemptNumber > MaxRetries)
            {
                _logger.LogWarning("Max retries ({MaxRetries}) exceeded for webhook event {EventId} of type {EventType}",
                    MaxRetries, eventId, eventType);
                return;
            }

            var nextRetryTime = GetNextRetryTime(attemptNumber);

            // Check if retry already exists
            var existingRetry = await _dbContext.WebhookRetries
                .FirstOrDefaultAsync(wr => wr.EventId == eventId && wr.Status == WebhookRetryStatus.Pending);

            if (existingRetry != null)
            {
                // Update existing retry
                existingRetry.AttemptNumber = attemptNumber;
                existingRetry.NextRetryAt = nextRetryTime;
                existingRetry.LastErrorMessage = errorMessage;
                existingRetry.UpdatedAt = DateTime.UtcNow;
                existingRetry.Status = WebhookRetryStatus.Pending;
            }
            else
            {
                // Create new retry record
                var webhookRetry = new WebhookRetry
                {
                    EventId = eventId,
                    EventType = eventType,
                    Payload = payload,
                    Signature = signature,
                    AttemptNumber = attemptNumber,
                    MaxRetries = MaxRetries,
                    NextRetryAt = nextRetryTime,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    LastErrorMessage = errorMessage,
                    Status = WebhookRetryStatus.Pending
                };

                _dbContext.WebhookRetries.Add(webhookRetry);
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Scheduled retry {AttemptNumber}/{MaxRetries} for webhook event {EventId} at {NextRetryTime}",
                attemptNumber, MaxRetries, eventId, nextRetryTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule retry for webhook event {EventId}: {ErrorMessage}",
                eventId, ex.Message);
        }
    }

    public async Task ProcessPendingRetriesAsync()
    {
        try
        {
            var currentTime = DateTime.UtcNow;
            var pendingRetries = await _dbContext.WebhookRetries
                .Where(wr => wr.Status == WebhookRetryStatus.Pending && 
                           wr.NextRetryAt <= currentTime)
                .OrderBy(wr => wr.NextRetryAt)
                .Take(50) // Process in batches to avoid overwhelming the system
                .ToListAsync();

            _logger.LogInformation("Processing {Count} pending webhook retries", pendingRetries.Count);

            foreach (var retry in pendingRetries)
            {
                await ProcessRetryAsync(retry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pending webhook retries: {ErrorMessage}", ex.Message);
        }
    }

    private async Task ProcessRetryAsync(WebhookRetry retry)
    {
        try
        {
            // Mark as processing to prevent concurrent processing
            retry.Status = WebhookRetryStatus.Processing;
            retry.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Processing retry attempt {AttemptNumber} for webhook event {EventId}",
                retry.AttemptNumber, retry.EventId);

            // Attempt to process the webhook
            await _webhookProcessor.ProcessWebhookAsync(retry.Payload, retry.Signature);

            // Success - mark as succeeded
            retry.Status = WebhookRetryStatus.Succeeded;
            retry.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Successfully processed retry for webhook event {EventId} after {AttemptNumber} attempts",
                retry.EventId, retry.AttemptNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Retry attempt {AttemptNumber} failed for webhook event {EventId}: {ErrorMessage}",
                retry.AttemptNumber, retry.EventId, ex.Message);

            if (retry.AttemptNumber >= retry.MaxRetries)
            {
                // Max retries exhausted
                retry.Status = WebhookRetryStatus.Exhausted;
                retry.LastErrorMessage = ex.Message;
                retry.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                _logger.LogError("Max retries exhausted for webhook event {EventId}. Final error: {ErrorMessage}",
                    retry.EventId, ex.Message);
            }
            else if (IsRetryableError(ex))
            {
                // Schedule next retry
                await ScheduleRetryAsync(retry.EventId, retry.EventType, retry.Payload, 
                    retry.Signature, ex.Message, retry.AttemptNumber + 1);
            }
            else
            {
                // Non-retryable error - mark as failed
                retry.Status = WebhookRetryStatus.Failed;
                retry.LastErrorMessage = ex.Message;
                retry.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                _logger.LogError("Non-retryable error for webhook event {EventId}: {ErrorMessage}",
                    retry.EventId, ex.Message);
            }
        }
    }

    public DateTime GetNextRetryTime(int attemptNumber)
    {
        if (attemptNumber <= 0) attemptNumber = 1;
        
        // Exponential backoff: BaseDelay * (BackoffMultiplier ^ (attemptNumber - 1))
        var delay = TimeSpan.FromTicks((long)(BaseDelay.Ticks * Math.Pow(BackoffMultiplier, attemptNumber - 1)));
        
        // Cap at maximum delay
        if (delay > MaxDelay)
            delay = MaxDelay;

        // Add jitter to prevent thundering herd (±25% randomization)
        var jitterMultiplier = 0.75 + (Random.Shared.NextDouble() * 0.5); // 0.75 to 1.25
        delay = TimeSpan.FromTicks((long)(delay.Ticks * jitterMultiplier));

        return DateTime.UtcNow.Add(delay);
    }

    public bool IsRetryableError(Exception exception)
    {
        // Check for Stripe-specific retryable errors
        if (exception is StripeException stripeEx)
        {
            // Check for retryable Stripe error codes
            if (!string.IsNullOrEmpty(stripeEx.StripeError?.Code) &&
                RetryableStripeErrorCodes.Contains(stripeEx.StripeError.Code))
            {
                return true;
            }

            // Check for HTTP status codes that indicate temporary issues
            return stripeEx.HttpStatusCode switch
            {
                System.Net.HttpStatusCode.InternalServerError => true,     // 500
                System.Net.HttpStatusCode.BadGateway => true,              // 502
                System.Net.HttpStatusCode.ServiceUnavailable => true,     // 503
                System.Net.HttpStatusCode.GatewayTimeout => true,         // 504
                System.Net.HttpStatusCode.TooManyRequests => true,        // 429
                _ => false
            };
        }

        // Check for network-related exceptions
        if (exception is HttpRequestException ||
            exception is TaskCanceledException ||
            exception is TimeoutException)
        {
            return true;
        }

        // Check for database-related temporary issues
        if (exception is DbUpdateException dbEx)
        {
            // Only retry if it's not a constraint violation or other permanent error
            var innerMessage = dbEx.InnerException?.Message?.ToLower() ?? "";
            if (innerMessage.Contains("timeout") || 
                innerMessage.Contains("connection") ||
                innerMessage.Contains("network"))
            {
                return true;
            }
        }

        return false;
    }
}