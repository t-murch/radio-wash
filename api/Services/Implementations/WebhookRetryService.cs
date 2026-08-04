using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Services.Implementations;

public class WebhookRetryService : IWebhookRetryService
{
  private readonly RadioWashDbContext _dbContext;
  private readonly ILogger<WebhookRetryService> _logger;
  private readonly IWebhookProcessor _webhookProcessor;
  private readonly IEventUtility _eventUtility;
  private readonly IIdempotencyService _idempotencyService;
  private readonly IDateTimeProvider _dateTimeProvider;
  private readonly IRandomProvider _randomProvider;
  private readonly IErrorClassifier _errorClassifier;

  // Configuration constants
  private const int DefaultMaxRetries = 5;
  private const int BaseDelayMinutes = 1;
  private const int MaxDelayMinutes = 60;
  private const double JitterFactor = 0.1;
  // A retry stuck in Processing longer than this (crash between the status write and the
  // outcome write) is picked up again by GetPendingRetriesAsync.
  private static readonly TimeSpan StaleProcessingThreshold = TimeSpan.FromMinutes(15);

  public WebhookRetryService(
    RadioWashDbContext dbContext,
    ILogger<WebhookRetryService> logger,
    IWebhookProcessor webhookProcessor,
    IEventUtility eventUtility,
    IIdempotencyService idempotencyService,
    IDateTimeProvider dateTimeProvider,
    IRandomProvider randomProvider,
    IErrorClassifier errorClassifier)
  {
    _dbContext = dbContext;
    _logger = logger;
    _webhookProcessor = webhookProcessor;
    _eventUtility = eventUtility;
    _idempotencyService = idempotencyService;
    _dateTimeProvider = dateTimeProvider;
    _randomProvider = randomProvider;
    _errorClassifier = errorClassifier;
  }

  public async Task ScheduleRetryAsync(string eventId, string eventType, string payload, string signature, string errorMessage, int attemptNumber = 1)
  {
    try
    {
      // Check if retry already exists for this event
      var existingRetry = await _dbContext.WebhookRetries
        .FirstOrDefaultAsync(wr => wr.EventId == eventId);

      if (existingRetry != null)
      {
        if (existingRetry.Status == WebhookRetryStatus.MaxRetriesExceeded)
        {
          // The internal loop already gave up on this event; Stripe's own redelivery (we
          // return 500 on failure) is the remaining recovery path. Re-arming here would let
          // every failing redelivery bypass the MaxRetries bound.
          _logger.LogInformation(
            "Not re-arming webhook retry for event {EventId}: internal retries exhausted, deferring to Stripe redelivery",
            eventId);
          return;
        }

        // Re-arm the retry, but never lower the attempt counter: a failing live redelivery
        // must not reset the internal loop's escalation and backoff progress.
        existingRetry.AttemptNumber = Math.Max(existingRetry.AttemptNumber, attemptNumber);
        existingRetry.LastErrorMessage = errorMessage;
        existingRetry.NextRetryAt = CalculateNextRetryTime(existingRetry.AttemptNumber);
        existingRetry.Status = WebhookRetryStatus.Pending;
        existingRetry.UpdatedAt = _dateTimeProvider.UtcNow;

        _dbContext.WebhookRetries.Update(existingRetry);
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
          MaxRetries = DefaultMaxRetries,
          Status = WebhookRetryStatus.Pending,
          NextRetryAt = CalculateNextRetryTime(attemptNumber),
          LastErrorMessage = errorMessage,
          CreatedAt = _dateTimeProvider.UtcNow,
          UpdatedAt = _dateTimeProvider.UtcNow
        };

        _dbContext.WebhookRetries.Add(webhookRetry);
      }

      await _dbContext.SaveChangesAsync();
      
      _logger.LogInformation("Scheduled webhook retry for event {EventId}, attempt {AttemptNumber}, next retry at {NextRetryAt}", 
        eventId, attemptNumber, CalculateNextRetryTime(attemptNumber));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to schedule webhook retry for event {EventId}: {ErrorMessage}", 
        eventId, ex.Message);
      throw;
    }
  }

  public async Task<IEnumerable<WebhookRetry>> GetPendingRetriesAsync()
  {
    try
    {
      var currentTime = _dateTimeProvider.UtcNow;
      var staleCutoff = currentTime - StaleProcessingThreshold;

      return await _dbContext.WebhookRetries
        .Where(wr => (wr.Status == WebhookRetryStatus.Pending &&
                      wr.NextRetryAt <= currentTime
                      || wr.Status == WebhookRetryStatus.Processing &&
                      wr.UpdatedAt <= staleCutoff) &&
                     wr.AttemptNumber <= wr.MaxRetries)
        .OrderBy(wr => wr.NextRetryAt)
        .Take(50) // Process in batches to avoid overwhelming system
        .ToListAsync();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to retrieve pending webhook retries: {ErrorMessage}", ex.Message);
      throw;
    }
  }

  public async Task ProcessRetryAsync(WebhookRetry retry)
  {
    // Mark as processing to prevent concurrent processing
    retry.Status = WebhookRetryStatus.Processing;
    retry.UpdatedAt = _dateTimeProvider.UtcNow;
    _dbContext.WebhookRetries.Update(retry);
    await _dbContext.SaveChangesAsync();

    // The idempotency claim gates this path too: a Stripe redelivery (we return 500 on
    // failure, so Stripe keeps redelivering) may be processing the same event right now.
    var claimed = await _idempotencyService.TryProcessEventAsync(retry.EventId, retry.EventType);
    if (!claimed)
    {
      // Another handler owns the event or it already succeeded. If that handler fails,
      // it schedules its own retry (updating this row back to Pending), so this attempt
      // can be closed out as superseded.
      _logger.LogInformation(
        "Webhook retry for event {EventId} superseded: event already processed or claimed by another handler",
        retry.EventId);
      await MarkRetrySupersededAsync(retry.Id);
      return;
    }

    try
    {
      _logger.LogInformation("Processing webhook retry for event {EventId}, attempt {AttemptNumber}",
        retry.EventId, retry.AttemptNumber);

      // The stored payload was signature-verified when first received; Stripe's timestamp
      // tolerance (5 min) makes re-verification impossible here by design, so parse only.
      var stripeEvent = _eventUtility.ParseEvent(retry.Payload);
      await _webhookProcessor.ProcessEventAsync(stripeEvent);

      await _idempotencyService.MarkEventSuccessfulAsync(retry.EventId);
      await MarkRetrySucceededAsync(retry.Id);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Webhook retry failed for event {EventId}, attempt {AttemptNumber}: {ErrorMessage}",
        retry.EventId, retry.AttemptNumber, ex.Message);

      // The two outcome writes are independent: a failure releasing the event claim must
      // not leave this retry row wedged in Processing (nothing else would resurrect it
      // before the stale sweep).
      try
      {
        await _idempotencyService.MarkEventFailedAsync(retry.EventId, ex.Message);
      }
      catch (Exception markEx)
      {
        _logger.LogError(markEx, "Failed to release idempotency claim for event {EventId} after retry failure",
          retry.EventId);
      }

      // Mark as failed and potentially schedule next retry
      await MarkRetryFailedAsync(retry.Id, ex.Message);
    }
  }

  private async Task MarkRetrySupersededAsync(int retryId)
  {
    var retry = await _dbContext.WebhookRetries.FindAsync(retryId);
    if (retry == null)
    {
      _logger.LogWarning("Webhook retry with ID {RetryId} not found when marking as superseded", retryId);
      return;
    }

    retry.Status = WebhookRetryStatus.Superseded;
    retry.UpdatedAt = _dateTimeProvider.UtcNow;

    _dbContext.WebhookRetries.Update(retry);
    await _dbContext.SaveChangesAsync();
  }

  public async Task MarkRetrySucceededAsync(int retryId)
  {
    try
    {
      var retry = await _dbContext.WebhookRetries.FindAsync(retryId);
      if (retry == null)
      {
        _logger.LogWarning("Webhook retry with ID {RetryId} not found when marking as succeeded", retryId);
        return;
      }

      retry.Status = WebhookRetryStatus.Succeeded;
      retry.UpdatedAt = _dateTimeProvider.UtcNow;
      
      _dbContext.WebhookRetries.Update(retry);
      await _dbContext.SaveChangesAsync();
      
      _logger.LogInformation("Webhook retry succeeded for event {EventId} after {AttemptNumber} attempts", 
        retry.EventId, retry.AttemptNumber);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to mark webhook retry {RetryId} as succeeded: {ErrorMessage}", 
        retryId, ex.Message);
      throw;
    }
  }

  public async Task MarkRetryFailedAsync(int retryId, string errorMessage)
  {
    try
    {
      var retry = await _dbContext.WebhookRetries.FindAsync(retryId);
      if (retry == null)
      {
        _logger.LogWarning("Webhook retry with ID {RetryId} not found when marking as failed", retryId);
        return;
      }

      retry.LastErrorMessage = errorMessage;
      retry.UpdatedAt = _dateTimeProvider.UtcNow;

      if (retry.AttemptNumber >= retry.MaxRetries)
      {
        // Max retries exceeded
        retry.Status = WebhookRetryStatus.MaxRetriesExceeded;
        _logger.LogError("Webhook retry for event {EventId} exceeded max retries ({MaxRetries}). Giving up.", 
          retry.EventId, retry.MaxRetries);
      }
      else
      {
        // Schedule next retry
        retry.AttemptNumber++;
        retry.NextRetryAt = CalculateNextRetryTime(retry.AttemptNumber);
        retry.Status = WebhookRetryStatus.Pending;
        
        _logger.LogInformation("Webhook retry for event {EventId} failed, scheduling retry {AttemptNumber} at {NextRetryAt}", 
          retry.EventId, retry.AttemptNumber, retry.NextRetryAt);
      }

      _dbContext.WebhookRetries.Update(retry);
      await _dbContext.SaveChangesAsync();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to mark webhook retry {RetryId} as failed: {ErrorMessage}", 
        retryId, ex.Message);
      throw;
    }
  }

  public bool IsRetryableError(Exception exception)
  {
    return _errorClassifier.IsRetryableError(exception);
  }

  public DateTime CalculateNextRetryTime(int attemptNumber)
  {
    // Exponential backoff: delay = BaseDelay * (2 ^ (attempt - 1))
    var exponentialDelay = BaseDelayMinutes * Math.Pow(2, attemptNumber - 1);
    
    // Cap at maximum delay
    var delayMinutes = Math.Min(exponentialDelay, MaxDelayMinutes);
    
    // Add jitter to prevent thundering herd problems
    var jitter = delayMinutes * JitterFactor * (_randomProvider.NextDouble() - 0.5) * 2;
    var finalDelayMinutes = delayMinutes + jitter;
    
    // Ensure minimum 30 seconds delay
    finalDelayMinutes = Math.Max(finalDelayMinutes, 0.5);
    
    return _dateTimeProvider.UtcNow.AddMinutes(finalDelayMinutes);
  }
}