using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Database-backed idempotency service for webhook events.
/// Relies on the DB unique constraint on EventId for concurrency protection.
/// </summary>
public class DatabaseIdempotencyService : IIdempotencyService
{
    private readonly RadioWashDbContext _dbContext;
    private readonly ILogger<DatabaseIdempotencyService> _logger;

    public DatabaseIdempotencyService(
        RadioWashDbContext dbContext,
        ILogger<DatabaseIdempotencyService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> TryProcessEventAsync(string eventId, string eventType)
    {
        // First check if event already exists in database
        var existingEvent = await _dbContext.ProcessedWebhookEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId);

        if (existingEvent != null)
        {
            if (existingEvent.IsSuccessful || existingEvent.IsPermanentlyFailed)
            {
                _logger.LogInformation("Webhook event {EventId} already processed (successful={Successful}, permanentlyFailed={PermanentlyFailed})",
                    eventId, existingEvent.IsSuccessful, existingEvent.IsPermanentlyFailed);
                return false;
            }

            // Failed event — attempt atomic update to claim re-processing rights
            var updated = await _dbContext.ProcessedWebhookEvents
                .Where(e => e.EventId == eventId && !e.IsSuccessful && !e.IsPermanentlyFailed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.ProcessedAt, DateTime.UtcNow)
                    .SetProperty(e => e.ErrorMessage, (string?)null));
            if (updated > 0)
            {
                _logger.LogWarning("Webhook event {EventId} previously failed. Claimed re-processing rights atomically", eventId);
                return true;
            }
            // updated == 0 means another request beat us or event was marked successful between our check and update
            _logger.LogInformation("Webhook event {EventId} already processed or claimed by concurrent request", eventId);
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

    public async Task MarkEventSuccessfulAsync(string eventId)
    {
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

    public async Task MarkEventPermanentlyFailedAsync(string eventId, string errorMessage)
    {
        try
        {
            var webhookEvent = await _dbContext.ProcessedWebhookEvents
                .FirstOrDefaultAsync(e => e.EventId == eventId);

            if (webhookEvent != null)
            {
                webhookEvent.IsPermanentlyFailed = true;
                webhookEvent.ErrorMessage = errorMessage;
                _dbContext.ProcessedWebhookEvents.Update(webhookEvent);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Marked webhook event {EventId} as permanently failed with error: {ErrorMessage}",
                    eventId, errorMessage);
            }
            else
            {
                _logger.LogWarning("Attempted to mark non-existent webhook event {EventId} as permanently failed", eventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark webhook event {EventId} as permanently failed", eventId);
            throw;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Check for PostgreSQL-specific exception with unique violation code
        if (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
            return true;

        // Fallback for SQLite (used in tests) and other providers
        var message = ex.InnerException?.Message;
        if (message == null) return false;
        if (message.Contains("duplicate key")) return true;
        if (message.Contains("UNIQUE constraint")) return true;
        return false;
    }
}
