using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Database-backed idempotency service with application-level locking for webhook events
/// </summary>
public class DatabaseIdempotencyService : IIdempotencyService, IDisposable
{
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
            // First check if event already exists in database
            var existingEvent = await _dbContext.ProcessedWebhookEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.EventId == eventId);

            if (existingEvent != null)
            {
                _logger.LogInformation(
                    "Webhook event {EventId} of type {EventType} has already been processed. Status: {IsSuccessful}",
                    eventId, eventType, existingEvent.IsSuccessful ? "Success" : "Failed");
                return false;
            }

            // Try to create the webhook event record to claim processing rights
            var webhookEvent = new ProcessedWebhookEvent
            {
                EventId = eventId,
                EventType = eventType,
                ProcessedAt = DateTime.UtcNow,
                IsSuccessful = false // Will be updated after successful processing
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
                _logger.LogInformation(
                    "Webhook event {EventId} of type {EventType} was already claimed by another concurrent request",
                    eventId, eventType);
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
                webhookEvent.IsSuccessful = true;
                webhookEvent.ErrorMessage = null;
                _dbContext.ProcessedWebhookEvents.Update(webhookEvent);
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
                webhookEvent.IsSuccessful = false;
                webhookEvent.ErrorMessage = errorMessage;
                _dbContext.ProcessedWebhookEvents.Update(webhookEvent);
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
        // Check for SQL Server unique constraint violation
        if (ex.InnerException?.Message?.Contains("duplicate key") == true ||
            ex.InnerException?.Message?.Contains("UNIQUE constraint") == true ||
            ex.InnerException?.Message?.Contains("unique constraint") == true)
        {
            return true;
        }

        // Check for SQLite unique constraint violation
        if (ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true)
        {
            return true;
        }

        // Check for PostgreSQL unique constraint violation
        if (ex.InnerException?.Message?.Contains("duplicate key value violates unique constraint") == true)
        {
            return true;
        }

        return false;
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