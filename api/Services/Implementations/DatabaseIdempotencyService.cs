using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Database-backed idempotency service for webhook events.
/// Relies on DB unique constraint on EventId for concurrency protection.
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
        // SELECT is a performance optimization; DB unique constraint on EventId is the concurrency guard
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

        var webhookEvent = new ProcessedWebhookEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow,
            IsSuccessful = false
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException?.Message?.Contains("duplicate key") == true ||
            ex.InnerException?.Message?.Contains("UNIQUE constraint") == true ||
            ex.InnerException?.Message?.Contains("unique constraint") == true)
        {
            return true;
        }

        if (ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true)
        {
            return true;
        }

        if (ex.InnerException?.Message?.Contains("duplicate key value violates unique constraint") == true)
        {
            return true;
        }

        return false;
    }
}
