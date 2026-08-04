using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class DatabaseIdempotencyServiceTests : IDisposable
{
    private readonly DbContextOptions<RadioWashDbContext> _dbOptions;
    private readonly RadioWashDbContext _dbContext;
    private readonly Mock<ILogger<DatabaseIdempotencyService>> _mockLogger;
    private readonly DatabaseIdempotencyService _idempotencyService;

    public DatabaseIdempotencyServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new RadioWashDbContext(_dbOptions);
        _dbContext.Database.EnsureCreated();

        _mockLogger = new Mock<ILogger<DatabaseIdempotencyService>>();
        _idempotencyService = new DatabaseIdempotencyService(_dbContext, _mockLogger.Object);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithNewEvent_ShouldClaimWithProcessingStatus()
    {
        // Arrange
        var eventId = "evt_new_event";
        var eventType = "customer.subscription.updated";

        // Act
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.True(result);

        var processedEvent = await _dbContext.ProcessedWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.Equal(eventType, processedEvent.EventType);
        Assert.Equal(WebhookEventStatus.Processing, processedEvent.Status);
        Assert.Equal(1, processedEvent.AttemptCount);
        Assert.NotNull(processedEvent.LastAttemptAt);
        Assert.Null(processedEvent.ErrorMessage);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithRecentProcessingClaim_ShouldReturnFalse()
    {
        // Arrange - first call leaves a fresh Processing claim
        var eventId = "evt_existing_event";
        var eventType = "customer.subscription.updated";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Act - second call while the claim is live
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithSucceededEvent_ShouldReturnFalse()
    {
        // Arrange
        var eventId = "evt_succeeded";
        var eventType = "customer.subscription.updated";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);
        await _idempotencyService.MarkEventSuccessfulAsync(eventId);

        // Act
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert - Succeeded is terminal
        Assert.False(result);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithFailedEvent_ShouldReClaim()
    {
        // Arrange - a failed attempt releases the claim
        var eventId = "evt_failed_reclaim";
        var eventType = "customer.subscription.updated";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);
        await _idempotencyService.MarkEventFailedAsync(eventId, "transient error");

        // Act - a redelivery/retry can claim the event again
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.True(result);

        var processedEvent = await _dbContext.ProcessedWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.Equal(WebhookEventStatus.Processing, processedEvent.Status);
        Assert.Equal(2, processedEvent.AttemptCount);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithFreshProcessingRow_ShouldReturnFalse()
    {
        // Arrange - a Processing row with a recent LastAttemptAt is owned by a live handler
        var eventId = "evt_fresh_processing";
        var eventType = "customer.subscription.updated";

        _dbContext.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow.AddMinutes(-5),
            Status = WebhookEventStatus.Processing,
            LastAttemptAt = DateTime.UtcNow.AddMinutes(-5),
            AttemptCount = 1
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithStaleProcessingRow_ShouldTakeOverClaim()
    {
        // Arrange - a Processing claim older than 15 minutes is treated as abandoned
        var eventId = "evt_stale_processing";
        var eventType = "customer.subscription.updated";
        var staleTimestamp = DateTime.UtcNow.AddMinutes(-20);

        _dbContext.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = staleTimestamp,
            Status = WebhookEventStatus.Processing,
            LastAttemptAt = staleTimestamp,
            AttemptCount = 1
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert - stale claim taken over
        Assert.True(result);

        var processedEvent = await _dbContext.ProcessedWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.Equal(WebhookEventStatus.Processing, processedEvent.Status);
        Assert.Equal(2, processedEvent.AttemptCount);
        Assert.True(processedEvent.LastAttemptAt > staleTimestamp);
    }

    [Fact]
    public async Task TryProcessEventAsync_ConcurrentCalls_ShouldOnlyAllowOneToProcess()
    {
        // Arrange
        var eventId = "evt_concurrent_test";
        var eventType = "customer.subscription.updated";

        // Act - Make two concurrent calls
        var task1 = _idempotencyService.TryProcessEventAsync(eventId, eventType);
        var task2 = _idempotencyService.TryProcessEventAsync(eventId, eventType);

        var results = await Task.WhenAll(task1, task2);

        // Assert - Only one should return true
        var allowedCount = results.Count(r => r);
        Assert.Equal(1, allowedCount);

        // Verify only one record exists in database
        var processedEvents = await _dbContext.ProcessedWebhookEvents
            .Where(e => e.EventId == eventId)
            .ToListAsync();
        Assert.Single(processedEvents);
    }

    [Fact]
    public async Task TryProcessEventAsync_ConcurrentTakeoverOfFailedRow_ShouldOnlyAllowOneToWin()
    {
        // Arrange - a Failed row visible to two independent service instances, each with its
        // own DbContext (simulating two app instances). Status is a concurrency token, so at
        // most one compare-and-swap to Processing can commit.
        var eventId = "evt_takeover_race";
        var eventType = "customer.subscription.updated";

        _dbContext.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow.AddMinutes(-2),
            Status = WebhookEventStatus.Failed,
            LastAttemptAt = DateTime.UtcNow.AddMinutes(-2),
            AttemptCount = 1,
            ErrorMessage = "previous failure"
        });
        await _dbContext.SaveChangesAsync();

        using var context1 = new RadioWashDbContext(_dbOptions);
        using var context2 = new RadioWashDbContext(_dbOptions);
        using var service1 = new DatabaseIdempotencyService(context1, _mockLogger.Object);
        using var service2 = new DatabaseIdempotencyService(context2, _mockLogger.Object);

        // Act - both instances race to re-claim the failed event
        var results = await Task.WhenAll(
            service1.TryProcessEventAsync(eventId, eventType),
            service2.TryProcessEventAsync(eventId, eventType));

        // Assert - exactly one wins; the loser either hit the concurrency token conflict or
        // observed the winner's fresh Processing claim.
        Assert.Equal(1, results.Count(r => r));

        using var verifyContext = new RadioWashDbContext(_dbOptions);
        var processedEvent = await verifyContext.ProcessedWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.Equal(WebhookEventStatus.Processing, processedEvent.Status);
        Assert.Equal(2, processedEvent.AttemptCount);
    }

    [Fact]
    public async Task MarkEventSuccessfulAsync_WithExistingEvent_ShouldSetSucceededStatus()
    {
        // Arrange
        var eventId = "evt_mark_success";
        var eventType = "customer.subscription.updated";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Act
        await _idempotencyService.MarkEventSuccessfulAsync(eventId);

        // Assert
        var processedEvent = await _dbContext.ProcessedWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.Equal(WebhookEventStatus.Succeeded, processedEvent.Status);
        Assert.Null(processedEvent.ErrorMessage);
    }

    [Fact]
    public async Task MarkEventSuccessfulAsync_AfterPriorFailure_ShouldClearErrorMessage()
    {
        // Arrange - fail once, re-claim, then succeed
        var eventId = "evt_success_after_failure";
        var eventType = "customer.subscription.updated";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);
        await _idempotencyService.MarkEventFailedAsync(eventId, "first attempt failed");
        await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Act
        await _idempotencyService.MarkEventSuccessfulAsync(eventId);

        // Assert
        var processedEvent = await _dbContext.ProcessedWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.Equal(WebhookEventStatus.Succeeded, processedEvent.Status);
        Assert.Null(processedEvent.ErrorMessage);
    }

    [Fact]
    public async Task MarkEventFailedAsync_WithExistingEvent_ShouldSetFailedStatusAndMessage()
    {
        // Arrange
        var eventId = "evt_mark_failed";
        var eventType = "customer.subscription.updated";
        var errorMessage = "Processing failed due to business logic error";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        var claimedAt = (await _dbContext.ProcessedWebhookEvents
            .FirstAsync(e => e.EventId == eventId)).LastAttemptAt;

        // Act
        await _idempotencyService.MarkEventFailedAsync(eventId, errorMessage);

        // Assert
        var processedEvent = await _dbContext.ProcessedWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.Equal(WebhookEventStatus.Failed, processedEvent.Status);
        Assert.Equal(errorMessage, processedEvent.ErrorMessage);
        Assert.NotNull(processedEvent.LastAttemptAt);
        Assert.True(processedEvent.LastAttemptAt >= claimedAt);
    }

    [Fact]
    public async Task MarkEventSuccessfulAsync_WithNonExistentEvent_ShouldNotThrow()
    {
        // Arrange
        var eventId = "evt_non_existent";

        // Act & Assert - Should not throw
        await _idempotencyService.MarkEventSuccessfulAsync(eventId);
    }

    [Fact]
    public async Task MarkEventFailedAsync_WithNonExistentEvent_ShouldNotThrow()
    {
        // Arrange
        var eventId = "evt_non_existent";
        var errorMessage = "Some error";

        // Act & Assert - Should not throw
        await _idempotencyService.MarkEventFailedAsync(eventId, errorMessage);
    }

    [Fact]
    public async Task TryProcessEventAsync_AfterDisposal_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _idempotencyService.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _idempotencyService.TryProcessEventAsync("evt_test", "test.event"));
    }

    [Fact]
    public async Task MarkEventSuccessfulAsync_AfterDisposal_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _idempotencyService.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _idempotencyService.MarkEventSuccessfulAsync("evt_test"));
    }

    [Fact]
    public async Task TryProcessEventAsync_WithPreExistingSucceededRecord_ShouldReturnFalse()
    {
        // Arrange
        var eventId = "evt_pre_existing";
        var eventType = "customer.subscription.updated";

        // Manually insert a record to simulate a previously completed event
        var existingEvent = new ProcessedWebhookEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow.AddMinutes(-5),
            Status = WebhookEventStatus.Succeeded,
            LastAttemptAt = DateTime.UtcNow.AddMinutes(-5),
            AttemptCount = 1
        };
        _dbContext.ProcessedWebhookEvents.Add(existingEvent);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.False(result);
    }

    public void Dispose()
    {
        _idempotencyService.Dispose();
        _dbContext.Dispose();
    }
}
