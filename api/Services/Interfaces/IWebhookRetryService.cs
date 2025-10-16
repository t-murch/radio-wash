using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

public interface IWebhookRetryService
{
    /// <summary>
    /// Schedules a webhook event for retry with exponential backoff
    /// </summary>
    /// <param name="eventId">The webhook event ID</param>
    /// <param name="eventType">The webhook event type</param>
    /// <param name="payload">The webhook payload</param>
    /// <param name="signature">The webhook signature</param>
    /// <param name="errorMessage">The error message from the failed attempt</param>
    /// <param name="attemptNumber">Current attempt number (1-based)</param>
    Task ScheduleRetryAsync(string eventId, string eventType, string payload, string signature, 
        string errorMessage, int attemptNumber);

    /// <summary>
    /// Processes retry attempts for due webhook events
    /// </summary>
    Task ProcessPendingRetriesAsync();

    /// <summary>
    /// Gets the next retry time based on attempt number and exponential backoff
    /// </summary>
    /// <param name="attemptNumber">Current attempt number (1-based)</param>
    DateTime GetNextRetryTime(int attemptNumber);

    /// <summary>
    /// Determines if an error is retryable based on the exception type and message
    /// </summary>
    /// <param name="exception">The exception that occurred</param>
    bool IsRetryableError(Exception exception);
}