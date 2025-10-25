namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Service for managing webhook event idempotency to prevent duplicate processing
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Attempts to process an event, ensuring idempotency across concurrent requests
    /// </summary>
    /// <param name="eventId">Unique identifier for the webhook event</param>
    /// <param name="eventType">Type of the webhook event</param>
    /// <returns>True if the event should be processed, false if already processed</returns>
    Task<bool> TryProcessEventAsync(string eventId, string eventType);

    /// <summary>
    /// Marks an event as successfully processed
    /// </summary>
    /// <param name="eventId">Unique identifier for the webhook event</param>
    Task MarkEventSuccessfulAsync(string eventId);

    /// <summary>
    /// Marks an event as failed with error details
    /// </summary>
    /// <param name="eventId">Unique identifier for the webhook event</param>
    /// <param name="errorMessage">Error message describing the failure</param>
    Task MarkEventFailedAsync(string eventId, string errorMessage);
}