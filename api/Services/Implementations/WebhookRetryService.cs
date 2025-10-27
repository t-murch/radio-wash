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
  private readonly IDateTimeProvider _dateTimeProvider;
  private readonly IRandomProvider _randomProvider;
  private readonly IErrorClassifier _errorClassifier;
  
  // Configuration constants
  private const int DefaultMaxRetries = 5;
  private const int BaseDelayMinutes = 1;
  private const int MaxDelayMinutes = 60;
  private const double JitterFactor = 0.1;

  public WebhookRetryService(
    RadioWashDbContext dbContext,
    ILogger<WebhookRetryService> logger,
    IWebhookProcessor webhookProcessor,
    IDateTimeProvider dateTimeProvider,
    IRandomProvider randomProvider,
    IErrorClassifier errorClassifier)
  {
    _dbContext = dbContext;
    _logger = logger;
    _webhookProcessor = webhookProcessor;
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
        // Update existing retry with new attempt
        existingRetry.AttemptNumber = attemptNumber;
        existingRetry.LastErrorMessage = errorMessage;
        existingRetry.NextRetryAt = CalculateNextRetryTime(attemptNumber);
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
      
      return await _dbContext.WebhookRetries
        .Where(wr => wr.Status == WebhookRetryStatus.Pending && 
                     wr.NextRetryAt <= currentTime &&
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
    try
    {
      // Mark as processing to prevent concurrent processing
      retry.Status = WebhookRetryStatus.Processing;
      retry.UpdatedAt = _dateTimeProvider.UtcNow;
      _dbContext.WebhookRetries.Update(retry);
      await _dbContext.SaveChangesAsync();

      _logger.LogInformation("Processing webhook retry for event {EventId}, attempt {AttemptNumber}", 
        retry.EventId, retry.AttemptNumber);

      // Process the webhook
      await _webhookProcessor.ProcessWebhookAsync(retry.Payload, retry.Signature);
      
      // Mark as succeeded
      await MarkRetrySucceededAsync(retry.Id);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Webhook retry failed for event {EventId}, attempt {AttemptNumber}: {ErrorMessage}", 
        retry.EventId, retry.AttemptNumber, ex.Message);
      
      // Mark as failed and potentially schedule next retry
      await MarkRetryFailedAsync(retry.Id, ex.Message);
    }
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