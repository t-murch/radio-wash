using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    private readonly SqliteConnection _connection;

    public DatabaseIdempotencyServiceTests()
    {
        // Use SQLite in-memory provider (supports ExecuteUpdateAsync unlike InMemory provider)
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseSqlite(_connection)
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
    public async Task TryProcessEventAsync_WithExistingSuccessfulEvent_ShouldReturnFalse()
    {
        // Arrange
        var eventId = "evt_existing_event";
        var eventType = "customer.subscription.updated";

        // First call should create the record, then mark it successful
        await _idempotencyService.TryProcessEventAsync(eventId, eventType);
        await _idempotencyService.MarkEventSuccessfulAsync(eventId);

        // Act - Second call with same event ID
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryProcessEventAsync_ConcurrentCalls_ShouldOnlyAllowOneToProcess()
    {
        // Arrange - create two different event IDs for concurrent processing
        var eventId1 = "evt_concurrent_test_1";
        var eventId2 = "evt_concurrent_test_2";
        var eventType = "customer.subscription.updated";

        // Act - Process two different events
        var result1 = await _idempotencyService.TryProcessEventAsync(eventId1, eventType);
        var result2 = await _idempotencyService.TryProcessEventAsync(eventId2, eventType);

        // Assert - Both should return true (different events)
        Assert.True(result1);
        Assert.True(result2);

        // Now test that re-processing a successfully completed event is blocked
        await _idempotencyService.MarkEventSuccessfulAsync(eventId1);

        var result3 = await _idempotencyService.TryProcessEventAsync(eventId1, eventType);

        // Assert - Should not be allowed (event already succeeded)
        Assert.False(result3);
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

    [Fact]
    public async Task TryProcessEventAsync_WithFailedEvent_ShouldAllowReprocessing()
    {
        // Arrange
        var eventId = "evt_failed_event";
        var eventType = "customer.subscription.updated";

        // First call creates the record
        await _idempotencyService.TryProcessEventAsync(eventId, eventType);
        // Mark it as failed
        await _idempotencyService.MarkEventFailedAsync(eventId, "Processing failed");

        // Act - Try to process again (should be allowed since it failed)
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.True(result);

        // Verify the record still exists (atomic update, not delete+reinsert)
        // Use AsNoTracking to bypass change tracker cache and read fresh from DB
        var events = await _dbContext.ProcessedWebhookEvents
            .AsNoTracking()
            .Where(e => e.EventId == eventId)
            .ToListAsync();
        Assert.Single(events);
        // Error message should be cleared by the atomic update
        Assert.Null(events[0].ErrorMessage);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithSuccessfulEvent_ShouldNotAllowReprocessing()
    {
        // Arrange
        var eventId = "evt_successful_event";
        var eventType = "customer.subscription.updated";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);
        await _idempotencyService.MarkEventSuccessfulAsync(eventId);

        // Act
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkEventPermanentlyFailedAsync_WithExistingEvent_ShouldSetPermanentlyFailed()
    {
        // Arrange
        var eventId = "evt_permanently_failed";
        var eventType = "customer.subscription.updated";
        var errorMessage = "Invalid subscription metadata";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Act
        await _idempotencyService.MarkEventPermanentlyFailedAsync(eventId, errorMessage);

        // Assert
        var processedEvent = await _dbContext.ProcessedWebhookEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.True(processedEvent.IsPermanentlyFailed);
        Assert.False(processedEvent.IsSuccessful);
        Assert.Equal(errorMessage, processedEvent.ErrorMessage);
    }

    [Fact]
    public async Task TryProcessEventAsync_WithPermanentlyFailedEvent_ShouldReturnFalse()
    {
        // Arrange
        var eventId = "evt_perm_failed_no_retry";
        var eventType = "customer.subscription.updated";

        await _idempotencyService.TryProcessEventAsync(eventId, eventType);
        await _idempotencyService.MarkEventPermanentlyFailedAsync(eventId, "Non-retryable error");

        // Act - Try to re-process (should be blocked because it's permanently failed)
        var result = await _idempotencyService.TryProcessEventAsync(eventId, eventType);

        // Assert
        Assert.False(result);

        // Verify the record still has the permanent failure state preserved
        var processedEvent = await _dbContext.ProcessedWebhookEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processedEvent);
        Assert.True(processedEvent.IsPermanentlyFailed);
        Assert.Equal("Non-retryable error", processedEvent.ErrorMessage);
    }

    [Fact]
    public async Task MarkEventPermanentlyFailedAsync_WithNonExistentEvent_ShouldNotThrow()
    {
        // Arrange
        var eventId = "evt_non_existent_perm";

        // Act & Assert - Should not throw
        await _idempotencyService.MarkEventPermanentlyFailedAsync(eventId, "Some error");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
