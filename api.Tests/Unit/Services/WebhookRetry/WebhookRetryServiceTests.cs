using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Tests.Unit.TestHelpers;
using Stripe;

namespace RadioWash.Api.Tests.Unit.Services.WebhookRetry;

public class WebhookRetryServiceTests : IDisposable
{
    private readonly Mock<ILogger<WebhookRetryService>> _mockLogger;
    private readonly Mock<IWebhookProcessor> _mockWebhookProcessor;
    private readonly Mock<IEventUtility> _mockEventUtility;
    private readonly Mock<IIdempotencyService> _mockIdempotencyService;
    private readonly Mock<IErrorClassifier> _mockErrorClassifier;
    private readonly TestDateTimeProvider _testDateTimeProvider;
    private readonly TestRandomProvider _testRandomProvider;
    private readonly RadioWashDbContext _dbContext;
    private readonly WebhookRetryService _webhookRetryService;

    public WebhookRetryServiceTests()
    {
        _mockLogger = new Mock<ILogger<WebhookRetryService>>();
        _mockWebhookProcessor = new Mock<IWebhookProcessor>();
        _mockEventUtility = new Mock<IEventUtility>();
        _mockIdempotencyService = new Mock<IIdempotencyService>();
        _mockErrorClassifier = new Mock<IErrorClassifier>();
        _testDateTimeProvider = new TestDateTimeProvider();
        _testRandomProvider = new TestRandomProvider();

        // Setup in-memory database with transaction warnings suppressed
        var options = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new RadioWashDbContext(options);
        _dbContext.Database.EnsureCreated();

        _webhookRetryService = new WebhookRetryService(
            _dbContext,
            _mockLogger.Object,
            _mockWebhookProcessor.Object,
            _mockEventUtility.Object,
            _mockIdempotencyService.Object,
            _testDateTimeProvider,
            _testRandomProvider,
            _mockErrorClassifier.Object);
    }

    #region CalculateNextRetryTime Tests

    [Theory]
    [InlineData(1, 1.0)]   // First attempt: 1 minute
    [InlineData(2, 2.0)]   // Second attempt: 2 minutes
    [InlineData(3, 4.0)]   // Third attempt: 4 minutes
    [InlineData(4, 8.0)]   // Fourth attempt: 8 minutes
    [InlineData(5, 16.0)]  // Fifth attempt: 16 minutes
    [InlineData(6, 32.0)]  // Sixth attempt: 32 minutes
    [InlineData(7, 60.0)]  // Seventh attempt: capped at 60 minutes
    [InlineData(10, 60.0)] // Large attempt: still capped at 60 minutes
    public void CalculateNextRetryTime_WithVariousAttempts_ShouldUseExponentialBackoff(int attemptNumber, double expectedDelayMinutes)
    {
        // Arrange
        _testRandomProvider.SetFixedValue(0.5); // No jitter (middle value cancels out)
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);

        // Act
        var result = _webhookRetryService.CalculateNextRetryTime(attemptNumber);

        // Assert
        var expectedTime = baseTime.AddMinutes(expectedDelayMinutes);
        Assert.Equal(expectedTime, result);
    }

    [Fact]
    public void CalculateNextRetryTime_WithJitterAtMinimum_ShouldApplyNegativeJitter()
    {
        // Arrange
        _testRandomProvider.SetFixedValue(0.0); // Minimum jitter value
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);

        // Act
        var result = _webhookRetryService.CalculateNextRetryTime(1);

        // Assert - Jitter calculation: 1.0 * 0.1 * (0.0 - 0.5) * 2 = -0.1 minutes
        // Final delay: 1.0 + (-0.1) = 0.9 minutes
        var expectedTime = baseTime.AddMinutes(0.9);
        Assert.Equal(expectedTime, result);
    }

    [Fact]
    public void CalculateNextRetryTime_WithJitterAtMaximum_ShouldApplyPositiveJitter()
    {
        // Arrange
        _testRandomProvider.SetFixedValue(0.999); // Near maximum jitter value
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);

        // Act
        var result = _webhookRetryService.CalculateNextRetryTime(1);

        // Assert - Jitter calculation: 1.0 * 0.1 * (0.999 - 0.5) * 2 = 0.0998 minutes
        // Final delay: 1.0 + 0.0998 = 1.0998 minutes
        var expectedTime = baseTime.AddMinutes(1.0998);
        Assert.Equal(expectedTime, result);
    }

    [Fact]
    public void CalculateNextRetryTime_WithSmallDelayAndNegativeJitter_ShouldEnforceMinimumDelay()
    {
        // Arrange
        _testRandomProvider.SetFixedValue(0.0); // Maximum negative jitter
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);

        // Act
        var result = _webhookRetryService.CalculateNextRetryTime(1);

        // Assert - Should not go below 0.5 minutes (30 seconds)
        Assert.True(result >= baseTime.AddMinutes(0.5));
    }

    #endregion

    #region Error Classification Tests

    [Fact]
    public void IsRetryableError_ShouldDelegateToErrorClassifier()
    {
        // Arrange
        var exception = new HttpRequestException("Network error");
        _mockErrorClassifier.Setup(x => x.IsRetryableError(exception)).Returns(true);

        // Act
        var result = _webhookRetryService.IsRetryableError(exception);

        // Assert
        Assert.True(result);
        _mockErrorClassifier.Verify(x => x.IsRetryableError(exception), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsRetryableError_ShouldReturnClassifierResult(bool classifierResult)
    {
        // Arrange
        var exception = new Exception("Test exception");
        _mockErrorClassifier.Setup(x => x.IsRetryableError(exception)).Returns(classifierResult);

        // Act
        var result = _webhookRetryService.IsRetryableError(exception);

        // Assert
        Assert.Equal(classifierResult, result);
        _mockErrorClassifier.Verify(x => x.IsRetryableError(exception), Times.Once);
    }

    #endregion

    #region ScheduleRetryAsync Tests

    [Fact]
    public async Task ScheduleRetryAsync_WithNewEvent_ShouldCreateRetryRecord()
    {
        // Arrange
        var eventId = "evt_test_123";
        var eventType = "customer.subscription.updated";
        var payload = "test_payload";
        var signature = "test_signature";
        var errorMessage = "Test error";
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);
        _testRandomProvider.SetFixedValue(0.5); // No jitter

        // Act
        await _webhookRetryService.ScheduleRetryAsync(eventId, eventType, payload, signature, errorMessage);

        // Assert
        var retry = await _dbContext.WebhookRetries.FirstOrDefaultAsync(wr => wr.EventId == eventId);
        Assert.NotNull(retry);
        Assert.Equal(eventType, retry.EventType);
        Assert.Equal(payload, retry.Payload);
        Assert.Equal(signature, retry.Signature);
        Assert.Equal(errorMessage, retry.LastErrorMessage);
        Assert.Equal(1, retry.AttemptNumber);
        Assert.Equal(5, retry.MaxRetries);
        Assert.Equal(WebhookRetryStatus.Pending, retry.Status);
        Assert.Equal(baseTime.AddMinutes(1), retry.NextRetryAt); // First attempt = 1 minute
        Assert.Equal(baseTime, retry.CreatedAt);
        Assert.Equal(baseTime, retry.UpdatedAt);
    }

    [Fact]
    public async Task ScheduleRetryAsync_WithExistingEvent_ShouldUpdateRetryRecord()
    {
        // Arrange
        var eventId = "evt_test_existing";
        var eventType = "customer.subscription.updated";
        var payload = "test_payload";
        var signature = "test_signature";
        var initialErrorMessage = "Initial error";
        var updatedErrorMessage = "Updated error";
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);
        _testRandomProvider.SetFixedValue(0.5);

        // Create initial retry
        await _webhookRetryService.ScheduleRetryAsync(eventId, eventType, payload, signature, initialErrorMessage);
        
        // Advance time
        _testDateTimeProvider.AdvanceTime(TimeSpan.FromMinutes(2));

        // Act - Schedule second attempt
        await _webhookRetryService.ScheduleRetryAsync(eventId, eventType, payload, signature, updatedErrorMessage, 2);

        // Assert
        var retries = await _dbContext.WebhookRetries.Where(wr => wr.EventId == eventId).ToListAsync();
        Assert.Single(retries); // Should still be one record
        
        var retry = retries.First();
        Assert.Equal(updatedErrorMessage, retry.LastErrorMessage);
        Assert.Equal(2, retry.AttemptNumber);
        Assert.Equal(WebhookRetryStatus.Pending, retry.Status);
        Assert.Equal(baseTime.AddMinutes(2).AddMinutes(2), retry.NextRetryAt); // Second attempt = 2 minutes
        Assert.Equal(baseTime.AddMinutes(2), retry.UpdatedAt);
    }

    [Fact]
    public async Task ScheduleRetryAsync_WithLowerAttemptNumber_ShouldNotResetEscalation()
    {
        // Arrange - the internal loop has already escalated this event to attempt 4; a
        // failing Stripe redelivery then calls ScheduleRetryAsync with the default attempt
        // number (1). The escalation and backoff progress must not be reset, otherwise the
        // MaxRetries bound never terminates while Stripe keeps redelivering.
        var eventId = "evt_no_reset";
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);
        _testRandomProvider.SetFixedValue(0.5);

        _dbContext.WebhookRetries.Add(new RadioWash.Api.Models.Domain.WebhookRetry
        {
            EventId = eventId,
            EventType = "customer.subscription.updated",
            Payload = "test_payload",
            Signature = "test_signature",
            AttemptNumber = 4,
            MaxRetries = 5,
            Status = WebhookRetryStatus.Failed,
            NextRetryAt = baseTime,
            LastErrorMessage = "attempt 4 failed",
            CreatedAt = baseTime.AddHours(-1),
            UpdatedAt = baseTime
        });
        await _dbContext.SaveChangesAsync();

        // Act - live redelivery failure re-arms with the default attemptNumber of 1
        await _webhookRetryService.ScheduleRetryAsync(
            eventId, "customer.subscription.updated", "test_payload", "test_signature", "redelivery failed");

        // Assert - counter kept at 4, backoff calculated from attempt 4 (8 minutes), re-armed
        var retry = await _dbContext.WebhookRetries.FirstAsync(wr => wr.EventId == eventId);
        Assert.Equal(4, retry.AttemptNumber);
        Assert.Equal(WebhookRetryStatus.Pending, retry.Status);
        Assert.Equal(baseTime.AddMinutes(8), retry.NextRetryAt); // 1 * 2^(4-1) = 8 minutes
    }

    [Fact]
    public async Task ScheduleRetryAsync_WhenMaxRetriesExceeded_ShouldNotReArm()
    {
        // Arrange - the internal loop already gave up; Stripe's own redelivery is the
        // remaining recovery path. Re-arming would bypass the MaxRetries bound on every
        // failing redelivery.
        var eventId = "evt_exhausted";
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);

        _dbContext.WebhookRetries.Add(new RadioWash.Api.Models.Domain.WebhookRetry
        {
            EventId = eventId,
            EventType = "customer.subscription.updated",
            Payload = "test_payload",
            Signature = "test_signature",
            AttemptNumber = 5,
            MaxRetries = 5,
            Status = WebhookRetryStatus.MaxRetriesExceeded,
            NextRetryAt = baseTime.AddHours(-1),
            LastErrorMessage = "gave up",
            CreatedAt = baseTime.AddHours(-2),
            UpdatedAt = baseTime.AddHours(-1)
        });
        await _dbContext.SaveChangesAsync();

        // Act
        await _webhookRetryService.ScheduleRetryAsync(
            eventId, "customer.subscription.updated", "test_payload", "test_signature", "redelivery failed");

        // Assert - untouched
        var retry = await _dbContext.WebhookRetries.FirstAsync(wr => wr.EventId == eventId);
        Assert.Equal(WebhookRetryStatus.MaxRetriesExceeded, retry.Status);
        Assert.Equal(5, retry.AttemptNumber);
        Assert.Equal("gave up", retry.LastErrorMessage);
    }

    #endregion

    #region GetPendingRetriesAsync Tests

    [Fact]
    public async Task GetPendingRetriesAsync_WithNoPendingRetries_ShouldReturnEmpty()
    {
        // Act
        var result = await _webhookRetryService.GetPendingRetriesAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPendingRetriesAsync_WithPendingRetriesDue_ShouldReturnThem()
    {
        // Arrange
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);

        var retry1 = new RadioWash.Api.Models.Domain.WebhookRetry
        {
            EventId = "evt_1",
            EventType = "test.event",
            Payload = "payload1",
            Signature = "sig1",
            AttemptNumber = 1,
            MaxRetries = 5,
            Status = WebhookRetryStatus.Pending,
            NextRetryAt = baseTime.AddMinutes(-1), // Due 1 minute ago
            LastErrorMessage = "Error 1",
            CreatedAt = baseTime.AddMinutes(-10),
            UpdatedAt = baseTime.AddMinutes(-10)
        };

        var retry2 = new RadioWash.Api.Models.Domain.WebhookRetry
        {
            EventId = "evt_2",
            EventType = "test.event",
            Payload = "payload2",
            Signature = "sig2",
            AttemptNumber = 2,
            MaxRetries = 5,
            Status = WebhookRetryStatus.Pending,
            NextRetryAt = baseTime, // Due now
            LastErrorMessage = "Error 2",
            CreatedAt = baseTime.AddMinutes(-5),
            UpdatedAt = baseTime.AddMinutes(-5)
        };

        _dbContext.WebhookRetries.AddRange(retry1, retry2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _webhookRetryService.GetPendingRetriesAsync();

        // Assert
        var retries = result.ToList();
        Assert.Equal(2, retries.Count);
        Assert.Contains(retries, r => r.EventId == "evt_1");
        Assert.Contains(retries, r => r.EventId == "evt_2");
    }

    [Fact]
    public async Task GetPendingRetriesAsync_WithFutureRetries_ShouldNotReturnThem()
    {
        // Arrange
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);

        var futureRetry = new RadioWash.Api.Models.Domain.WebhookRetry
        {
            EventId = "evt_future",
            EventType = "test.event",
            Payload = "payload",
            Signature = "sig",
            AttemptNumber = 1,
            MaxRetries = 5,
            Status = WebhookRetryStatus.Pending,
            NextRetryAt = baseTime.AddMinutes(5), // Due in 5 minutes
            LastErrorMessage = "Error",
            CreatedAt = baseTime,
            UpdatedAt = baseTime
        };

        _dbContext.WebhookRetries.Add(futureRetry);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _webhookRetryService.GetPendingRetriesAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPendingRetriesAsync_WithMaxRetriesExceeded_ShouldNotReturnThem()
    {
        // Arrange
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);

        var maxedRetry = new RadioWash.Api.Models.Domain.WebhookRetry
        {
            EventId = "evt_maxed",
            EventType = "test.event",
            Payload = "payload",
            Signature = "sig",
            AttemptNumber = 6, // Exceeds max of 5
            MaxRetries = 5,
            Status = WebhookRetryStatus.Pending,
            NextRetryAt = baseTime.AddMinutes(-1),
            LastErrorMessage = "Error",
            CreatedAt = baseTime,
            UpdatedAt = baseTime
        };

        _dbContext.WebhookRetries.Add(maxedRetry);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _webhookRetryService.GetPendingRetriesAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPendingRetriesAsync_ShouldLimitToBatchSize()
    {
        // Arrange
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);

        // Create 60 pending retries (more than batch size of 50)
        var retries = Enumerable.Range(1, 60).Select(i => new RadioWash.Api.Models.Domain.WebhookRetry
        {
            EventId = $"evt_{i}",
            EventType = "test.event",
            Payload = $"payload{i}",
            Signature = $"sig{i}",
            AttemptNumber = 1,
            MaxRetries = 5,
            Status = WebhookRetryStatus.Pending,
            NextRetryAt = baseTime.AddMinutes(-1),
            LastErrorMessage = $"Error {i}",
            CreatedAt = baseTime,
            UpdatedAt = baseTime
        });

        _dbContext.WebhookRetries.AddRange(retries);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _webhookRetryService.GetPendingRetriesAsync();

        // Assert
        Assert.Equal(50, result.Count()); // Should be limited to batch size
    }

    #endregion

    #region ProcessRetryAsync Tests

    [Fact]
    public async Task ProcessRetryAsync_WhenEventAlreadyClaimed_ShouldMarkSupersededWithoutProcessing()
    {
        // Arrange
        var retry = await AddRetryAsync("evt_superseded");
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(retry.EventId, retry.EventType))
            .ReturnsAsync(false);

        // Act
        await _webhookRetryService.ProcessRetryAsync(retry);

        // Assert - closed out as Superseded (not Succeeded: this retry did no work) without
        // touching the processor
        var stored = await _dbContext.WebhookRetries.FindAsync(retry.Id);
        Assert.Equal(WebhookRetryStatus.Superseded, stored!.Status);
        _mockEventUtility.Verify(x => x.ParseEvent(It.IsAny<string>()), Times.Never);
        _mockWebhookProcessor.Verify(x => x.ProcessEventAsync(It.IsAny<Event>()), Times.Never);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessRetryAsync_WhenClaimed_ShouldParseWithoutSignatureAndProcess()
    {
        // Arrange
        var retry = await AddRetryAsync("evt_claimed");
        var parsedEvent = new Event { Id = retry.EventId, Type = retry.EventType };

        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(retry.EventId, retry.EventType))
            .ReturnsAsync(true);
        _mockEventUtility.Setup(x => x.ParseEvent(retry.Payload)).Returns(parsedEvent);

        // Act
        await _webhookRetryService.ProcessRetryAsync(retry);

        // Assert
        _mockEventUtility.Verify(x => x.ParseEvent(retry.Payload), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessEventAsync(parsedEvent), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(retry.EventId), Times.Once);

        var stored = await _dbContext.WebhookRetries.FindAsync(retry.Id);
        Assert.Equal(WebhookRetryStatus.Succeeded, stored!.Status);

        // The stored payload was verified on first receipt; the retry path must never
        // attempt signature re-verification.
        _mockEventUtility.Verify(x => x.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessRetryAsync_WithStaleSignatureTimestamp_ShouldStillProcess()
    {
        // Arrange - A stored payload whose Stripe-Signature timestamp is far outside the
        // 5-minute tolerance. Re-verification would be impossible; only parsing is used.
        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook("sub_123", "active");
        var staleTimestamp = DateTimeOffset.UtcNow.AddSeconds(-600).ToUnixTimeSeconds();
        var staleSignature = $"t={staleTimestamp},v1=deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

        var retry = await AddRetryAsync("evt_123", payload, staleSignature);
        var parsedEvent = EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);

        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(retry.EventId, retry.EventType))
            .ReturnsAsync(true);
        _mockEventUtility.Setup(x => x.ParseEvent(payload)).Returns(parsedEvent);

        // Act
        await _webhookRetryService.ProcessRetryAsync(retry);

        // Assert - Processed despite the >300-second-old signature timestamp
        _mockWebhookProcessor.Verify(x => x.ProcessEventAsync(parsedEvent), Times.Once);
        _mockEventUtility.Verify(x => x.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        var stored = await _dbContext.WebhookRetries.FindAsync(retry.Id);
        Assert.Equal(WebhookRetryStatus.Succeeded, stored!.Status);
    }

    [Fact]
    public async Task ProcessRetryAsync_WhenProcessorThrows_ShouldMarkEventFailedAndScheduleNextRetry()
    {
        // Arrange
        var baseTime = new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        _testDateTimeProvider.SetUtcNow(baseTime);
        _testRandomProvider.SetFixedValue(0.5); // No jitter

        var retry = await AddRetryAsync("evt_failing");
        var parsedEvent = new Event { Id = retry.EventId, Type = retry.EventType };

        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(retry.EventId, retry.EventType))
            .ReturnsAsync(true);
        _mockEventUtility.Setup(x => x.ParseEvent(retry.Payload)).Returns(parsedEvent);
        _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(parsedEvent))
            .ThrowsAsync(new InvalidOperationException("Processing failed"));

        // Act
        await _webhookRetryService.ProcessRetryAsync(retry);

        // Assert
        _mockIdempotencyService.Verify(x => x.MarkEventFailedAsync(retry.EventId, "Processing failed"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(It.IsAny<string>()), Times.Never);

        var stored = await _dbContext.WebhookRetries.FindAsync(retry.Id);
        Assert.Equal(WebhookRetryStatus.Pending, stored!.Status);
        Assert.Equal(2, stored.AttemptNumber);
        Assert.Equal("Processing failed", stored.LastErrorMessage);
        Assert.Equal(baseTime.AddMinutes(2), stored.NextRetryAt); // Second attempt = 2 minutes
    }

    [Fact]
    public async Task ProcessRetryAsync_WhenProcessorThrowsAtMaxRetries_ShouldMarkMaxRetriesExceeded()
    {
        // Arrange
        var retry = await AddRetryAsync("evt_maxed_out", attemptNumber: 5);
        var parsedEvent = new Event { Id = retry.EventId, Type = retry.EventType };

        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(retry.EventId, retry.EventType))
            .ReturnsAsync(true);
        _mockEventUtility.Setup(x => x.ParseEvent(retry.Payload)).Returns(parsedEvent);
        _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(parsedEvent))
            .ThrowsAsync(new InvalidOperationException("Still failing"));

        // Act
        await _webhookRetryService.ProcessRetryAsync(retry);

        // Assert
        _mockIdempotencyService.Verify(x => x.MarkEventFailedAsync(retry.EventId, "Still failing"), Times.Once);

        var stored = await _dbContext.WebhookRetries.FindAsync(retry.Id);
        Assert.Equal(WebhookRetryStatus.MaxRetriesExceeded, stored!.Status);
        Assert.Equal(5, stored.AttemptNumber);
    }

    private async Task<RadioWash.Api.Models.Domain.WebhookRetry> AddRetryAsync(
        string eventId,
        string payload = "test_payload",
        string signature = "test_signature",
        int attemptNumber = 1)
    {
        var now = _testDateTimeProvider.UtcNow;
        var retry = new RadioWash.Api.Models.Domain.WebhookRetry
        {
            EventId = eventId,
            EventType = "customer.subscription.updated",
            Payload = payload,
            Signature = signature,
            AttemptNumber = attemptNumber,
            MaxRetries = 5,
            Status = WebhookRetryStatus.Pending,
            NextRetryAt = now.AddMinutes(-1),
            LastErrorMessage = "Initial error",
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now.AddMinutes(-10)
        };

        _dbContext.WebhookRetries.Add(retry);
        await _dbContext.SaveChangesAsync();
        return retry;
    }

    #endregion

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}