using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Service for managing webhook retry functionality with exponential backoff
/// </summary>
public interface IWebhookRetryService
{
  /// <summary>
  /// Schedules a webhook for retry with exponential backoff
  /// </summary>
  Task ScheduleRetryAsync(string eventId, string eventType, string payload, string signature, string errorMessage, int attemptNumber = 1);
  
  /// <summary>
  /// Gets pending webhook retries that are ready for processing
  /// </summary>
  Task<IEnumerable<WebhookRetry>> GetPendingRetriesAsync();
  
  /// <summary>
  /// Processes a webhook retry attempt
  /// </summary>
  Task ProcessRetryAsync(WebhookRetry retry);
  
  /// <summary>
  /// Marks a retry as succeeded
  /// </summary>
  Task MarkRetrySucceededAsync(int retryId);
  
  /// <summary>
  /// Marks a retry as failed and schedules next attempt if retries remain
  /// </summary>
  Task MarkRetryFailedAsync(int retryId, string errorMessage);
  
  /// <summary>
  /// Determines if an error is retryable
  /// </summary>
  bool IsRetryableError(Exception exception);
  
  /// <summary>
  /// Calculates the next retry time using exponential backoff with jitter
  /// </summary>
  DateTime CalculateNextRetryTime(int attemptNumber);
}