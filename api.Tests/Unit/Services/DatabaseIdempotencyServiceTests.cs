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
    private readonly RadioWashDbContext _dbContext;
    private readonly Mock<ILogger<DatabaseIdempotencyService>> _mockLogger;
    private readonly DatabaseIdempotencyService _idempotencyService;

    public DatabaseIdempotencyServiceTests()
    {
        var options = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new RadioWashDbContext(options);
        _dbContext.Database.EnsureCreated();

        _mockLogger = new Mock<ILogger<DatabaseIdempotencyService>>();
        _idempotencyService = new DatabaseIdempotencyService(_dbContext, _mockLogger.Object);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithNewEvent_ShouldReturnTrueAndCreateRecord()
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
        Assert.False(processedEvent.IsSuccessful);
        Assert.Null(processedEvent.ErrorMessage);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithExistingEvent_ShouldReturnFalse()
    {
        // Arrange
        var eventId = "evt_existing_event";
        var eventType = "customer.subscription.updated";

        // First call should create the record
        await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Act - Second call with same event ID
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.False(result);
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
    public async Task MarkEventSuccessfulAsync_WithExistingEvent_ShouldUpdateSuccessFlag()
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
        Assert.True(processedEvent.IsSuccessful);
        Assert.Null(processedEvent.ErrorMessage);
    }

    [Fact]
    public async Task MarkEventFailedAsync_WithExistingEvent_ShouldUpdateFailureFlagAndMessage()
    {
        // Arrange
        var eventId = "evt_mark_failed";
        var eventType = "customer.subscription.updated";
        var errorMessage = "Processing failed due to business logic error";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Act
        await _idempotencyService.MarkEventFailedAsync(eventId, errorMessage);

        // Assert
        var processedEvent = await _dbContext.ProcessedWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.False(processedEvent.IsSuccessful);
        Assert.Equal(errorMessage, processedEvent.ErrorMessage);
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
    public async Task TryProcessEventAsync_WithPreExistingDatabaseRecord_ShouldReturnFalse()
    {
        // Arrange
        var eventId = "evt_pre_existing";
        var eventType = "customer.subscription.updated";

        // Manually insert a record to simulate pre-existing event
        var existingEvent = new ProcessedWebhookEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow.AddMinutes(-5),
            IsSuccessful = true
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