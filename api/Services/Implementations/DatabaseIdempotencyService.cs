using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Database-backed idempotency service with application-level locking for webhook events.
/// A row acts as a processing claim: Processing rows are owned by a live handler, Succeeded
/// rows are terminal, and Failed rows are re-claimable so Stripe redeliveries and internal
/// retries can attempt the event again. Stale Processing rows (crashed instance) are taken
/// over after a threshold. Takeovers are compare-and-swaps on Status (a concurrency token),
/// so two claimants can never both win.
/// </summary>
public class DatabaseIdempotencyService : IIdempotencyService, IDisposable
{
    // A Processing claim older than this is assumed abandoned (the owning instance died
    // mid-event) and may be taken over. Normal webhook processing completes in seconds.
    private static readonly TimeSpan StaleClaimThreshold = TimeSpan.FromMinutes(15);

    private readonly RadioWashDbContext _dbContext;
    private readonly ILogger<DatabaseIdempotencyService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _eventLocks = new();
    private readonly SemaphoreSlim _lockCleanupSemaphore = new(1, 1);
    private volatile bool _disposed = false;

    public DatabaseIdempotencyService(
        RadioWashDbContext dbContext,
        ILogger<DatabaseIdempotencyService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> TryProcessEventAsync(string eventId, string eventType)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DatabaseIdempotencyService));
        }

        // Get or create a semaphore for this specific event ID
        var eventLock = _eventLocks.GetOrAdd(eventId, _ => new SemaphoreSlim(1, 1));

        // Acquire the lock for this event
        await eventLock.WaitAsync();

        try
        {
            var existingEvent = await _dbContext.ProcessedWebhookEvents
                .FirstOrDefaultAsync(e => e.EventId == eventId);

            if (existingEvent == null)
            {
                return await TryInsertClaimAsync(eventId, eventType);
            }

            switch (existingEvent.Status)
            {
                case WebhookEventStatus.Succeeded:
                    _logger.LogInformation(
                        "Webhook event {EventId} of type {EventType} has already been processed successfully",
                        eventId, eventType);
                    return false;

                case WebhookEventStatus.Failed:
                    return await TryTakeOverClaimAsync(existingEvent, "previous attempt failed");

                case WebhookEventStatus.Processing:
                    var claimAge = DateTime.UtcNow - (existingEvent.LastAttemptAt ?? existingEvent.ProcessedAt);
                    if (claimAge > StaleClaimThreshold)
                    {
                        return await TryTakeOverClaimAsync(existingEvent, $"stale claim ({claimAge.TotalMinutes:F0} min old)");
                    }

                    _logger.LogInformation(
                        "Webhook event {EventId} of type {EventType} is currently being processed by another handler",
                        eventId, eventType);
                    return false;

                default:
                    return false;
            }
        }
        finally
        {
            eventLock.Release();

            // Clean up the semaphore if no one else is waiting
            await CleanupEventLockIfUnusedAsync(eventId);
        }
    }

    private async Task<bool> TryInsertClaimAsync(string eventId, string eventType)
    {
        var webhookEvent = new ProcessedWebhookEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow,
            Status = WebhookEventStatus.Processing,
            LastAttemptAt = DateTime.UtcNow,
            AttemptCount = 1
        };

        try
        {
            _dbContext.ProcessedWebhookEvents.Add(webhookEvent);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Successfully claimed processing rights for webhook event {EventId} of type {EventType}",
                eventId, eventType);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Another concurrent request already claimed this event
            _dbContext.Entry(webhookEvent).State = EntityState.Detached;
            _logger.LogInformation(
                "Webhook event {EventId} of type {EventType} was already claimed by another concurrent request",
                eventId, eventType);
            return false;
        }
    }

    private async Task<bool> TryTakeOverClaimAsync(ProcessedWebhookEvent existingEvent, string reason)
    {
        // Status is a concurrency token, so this update only commits if the row still holds
        // the status we read — a concurrent takeover loses with DbUpdateConcurrencyException.
        existingEvent.Status = WebhookEventStatus.Processing;
        existingEvent.LastAttemptAt = DateTime.UtcNow;
        existingEvent.AttemptCount++;

        try
        {
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Re-claimed webhook event {EventId} for processing ({Reason}), attempt {AttemptCount}",
                existingEvent.EventId, reason, existingEvent.AttemptCount);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.Entry(existingEvent).State = EntityState.Detached;
            _logger.LogInformation(
                "Webhook event {EventId} was re-claimed by another concurrent handler",
                existingEvent.EventId);
            return false;
        }
    }

    public async Task MarkEventSuccessfulAsync(string eventId)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DatabaseIdempotencyService));
        }

        try
        {
            var webhookEvent = await _dbContext.ProcessedWebhookEvents
                .FirstOrDefaultAsync(e => e.EventId == eventId);

            if (webhookEvent != null)
            {
                webhookEvent.Status = WebhookEventStatus.Succeeded;
                webhookEvent.ErrorMessage = null;
                webhookEvent.ProcessedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Marked webhook event {EventId} as successfully processed", eventId);
            }
            else
            {
                _logger.LogWarning("Attempted to mark non-existent webhook event {EventId} as successful", eventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark webhook event {EventId} as successful", eventId);
            throw;
        }
    }

    public async Task MarkEventFailedAsync(string eventId, string errorMessage)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DatabaseIdempotencyService));
        }

        try
        {
            var webhookEvent = await _dbContext.ProcessedWebhookEvents
                .FirstOrDefaultAsync(e => e.EventId == eventId);

            if (webhookEvent != null)
            {
                // Failed releases the claim: Stripe redelivery (we return 500) or the
                // internal retry loop can re-claim and try again.
                webhookEvent.Status = WebhookEventStatus.Failed;
                webhookEvent.ErrorMessage = errorMessage;
                webhookEvent.LastAttemptAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Marked webhook event {EventId} as failed with error: {ErrorMessage}",
                    eventId, errorMessage);
            }
            else
            {
                _logger.LogWarning("Attempted to mark non-existent webhook event {EventId} as failed", eventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark webhook event {EventId} as failed", eventId);
            throw;
        }
    }

    private async Task CleanupEventLockIfUnusedAsync(string eventId)
    {
        await _lockCleanupSemaphore.WaitAsync();
        try
        {
            if (_eventLocks.TryGetValue(eventId, out var semaphore))
            {
                // If no one is waiting and the current count is 1 (meaning it's available)
                if (semaphore.CurrentCount == 1)
                {
                    if (_eventLocks.TryRemove(eventId, out var removedSemaphore))
                    {
                        removedSemaphore.Dispose();
                    }
                }
            }
        }
        finally
        {
            _lockCleanupSemaphore.Release();
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Postgres reports unique violations via SqlState 23505 — match on that, not on
        // locale-dependent message text.
        if (ex.InnerException is Npgsql.PostgresException pgEx)
        {
            return pgEx.SqlState == "23505";
        }

        // Fallback for the EF InMemory provider used in unit tests, which surfaces unique
        // index violations as a plain exception message.
        return ex.InnerException?.Message?.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.Message.Contains("same key", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dispose all semaphores
        foreach (var semaphore in _eventLocks.Values)
        {
            semaphore.Dispose();
        }
        _eventLocks.Clear();

        _lockCleanupSemaphore.Dispose();
    }
}
