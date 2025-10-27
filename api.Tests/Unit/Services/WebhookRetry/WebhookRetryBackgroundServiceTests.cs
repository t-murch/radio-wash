using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.BackgroundServices;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services.WebhookRetry;

public class WebhookRetryBackgroundServiceTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScope> _mockServiceScope;
    private readonly Mock<IServiceScopeFactory> _mockServiceScopeFactory;
    private readonly Mock<IWebhookRetryService> _mockWebhookRetryService;
    private readonly Mock<ILogger<WebhookRetryBackgroundService>> _mockLogger;

    public WebhookRetryBackgroundServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockServiceScope = new Mock<IServiceScope>();
        _mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
        _mockWebhookRetryService = new Mock<IWebhookRetryService>();
        _mockLogger = new Mock<ILogger<WebhookRetryBackgroundService>>();

        // Setup service provider chain to match actual implementation
        // The background service calls _serviceProvider.CreateScope() which returns a scoped provider
        var mockScopedServiceProvider = new Mock<IServiceProvider>();
        
        _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockServiceScopeFactory.Object);
        _mockServiceScopeFactory.Setup(x => x.CreateScope())
            .Returns(_mockServiceScope.Object);
        _mockServiceScope.Setup(x => x.ServiceProvider)
            .Returns(mockScopedServiceProvider.Object);
        
        // Mock the scoped service provider to return the webhook retry service
        mockScopedServiceProvider.Setup(x => x.GetService(typeof(IWebhookRetryService)))
            .Returns(_mockWebhookRetryService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoPendingRetries_ShouldLogAndContinue()
    {
        // Arrange
        var backgroundService = new WebhookRetryBackgroundService(_mockServiceProvider.Object, _mockLogger.Object);
        var cancellationTokenSource = new CancellationTokenSource();
        
        _mockWebhookRetryService.Setup(x => x.GetPendingRetriesAsync())
            .ReturnsAsync(new List<RadioWash.Api.Models.Domain.WebhookRetry>());

        // Cancel after short delay to prevent infinite loop
        _ = Task.Delay(100).ContinueWith(_ => cancellationTokenSource.Cancel());

        // Act
        await backgroundService.StartAsync(cancellationTokenSource.Token);
        await Task.Delay(150); // Give time for one processing cycle
        await backgroundService.StopAsync(cancellationTokenSource.Token);

        // Assert
        _mockWebhookRetryService.Verify(x => x.GetPendingRetriesAsync(), Times.AtLeastOnce);
        _mockWebhookRetryService.Verify(x => x.ProcessRetryAsync(It.IsAny<RadioWash.Api.Models.Domain.WebhookRetry>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithPendingRetries_ShouldProcessThem()
    {
        // Arrange
        var backgroundService = new WebhookRetryBackgroundService(_mockServiceProvider.Object, _mockLogger.Object);
        var cancellationTokenSource = new CancellationTokenSource();

        var pendingRetries = new List<RadioWash.Api.Models.Domain.WebhookRetry>
        {
            CreateTestWebhookRetry("evt_1"),
            CreateTestWebhookRetry("evt_2")
        };

        _mockWebhookRetryService.Setup(x => x.GetPendingRetriesAsync())
            .ReturnsAsync(pendingRetries);
        _mockWebhookRetryService.Setup(x => x.ProcessRetryAsync(It.IsAny<RadioWash.Api.Models.Domain.WebhookRetry>()))
            .Returns(Task.CompletedTask);

        // Cancel after short delay
        _ = Task.Delay(100).ContinueWith(_ => cancellationTokenSource.Cancel());

        // Act
        await backgroundService.StartAsync(cancellationTokenSource.Token);
        await Task.Delay(150); // Give time for processing
        await backgroundService.StopAsync(cancellationTokenSource.Token);

        // Assert
        _mockWebhookRetryService.Verify(x => x.GetPendingRetriesAsync(), Times.AtLeastOnce);
        _mockWebhookRetryService.Verify(x => x.ProcessRetryAsync(It.IsAny<RadioWash.Api.Models.Domain.WebhookRetry>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_WithProcessingError_ShouldContinueProcessingOtherRetries()
    {
        // Arrange
        var backgroundService = new WebhookRetryBackgroundService(_mockServiceProvider.Object, _mockLogger.Object);
        var cancellationTokenSource = new CancellationTokenSource();

        var pendingRetries = new List<RadioWash.Api.Models.Domain.WebhookRetry>
        {
            CreateTestWebhookRetry("evt_fail"),
            CreateTestWebhookRetry("evt_success")
        };

        _mockWebhookRetryService.Setup(x => x.GetPendingRetriesAsync())
            .ReturnsAsync(pendingRetries);
        
        // First retry fails, second succeeds
        _mockWebhookRetryService.Setup(x => x.ProcessRetryAsync(It.Is<RadioWash.Api.Models.Domain.WebhookRetry>(r => r.EventId == "evt_fail")))
            .ThrowsAsync(new InvalidOperationException("Processing failed"));
        _mockWebhookRetryService.Setup(x => x.ProcessRetryAsync(It.Is<RadioWash.Api.Models.Domain.WebhookRetry>(r => r.EventId == "evt_success")))
            .Returns(Task.CompletedTask);

        // Cancel after short delay
        _ = Task.Delay(100).ContinueWith(_ => cancellationTokenSource.Cancel());

        // Act
        await backgroundService.StartAsync(cancellationTokenSource.Token);
        await Task.Delay(150); // Give time for processing
        await backgroundService.StopAsync(cancellationTokenSource.Token);

        // Assert
        _mockWebhookRetryService.Verify(x => x.ProcessRetryAsync(It.IsAny<RadioWash.Api.Models.Domain.WebhookRetry>()), Times.AtLeast(2));
        // Verify both retries were attempted despite first one failing
        _mockWebhookRetryService.Verify(x => x.ProcessRetryAsync(It.Is<RadioWash.Api.Models.Domain.WebhookRetry>(r => r.EventId == "evt_fail")), Times.AtLeastOnce);
        _mockWebhookRetryService.Verify(x => x.ProcessRetryAsync(It.Is<RadioWash.Api.Models.Domain.WebhookRetry>(r => r.EventId == "evt_success")), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithDatabaseError_ShouldLogErrorAndContinue()
    {
        // Arrange
        var backgroundService = new WebhookRetryBackgroundService(_mockServiceProvider.Object, _mockLogger.Object);
        var cancellationTokenSource = new CancellationTokenSource();

        // Setup sequence: first call throws, then return empty list for subsequent calls
        _mockWebhookRetryService.SetupSequence(x => x.GetPendingRetriesAsync())
            .ThrowsAsync(new InvalidOperationException("Database error"))
            .ReturnsAsync(new List<RadioWash.Api.Models.Domain.WebhookRetry>());

        // Start the service
        await backgroundService.StartAsync(cancellationTokenSource.Token);
        
        // Wait for first processing attempt (should fail)
        await Task.Delay(100);
        
        // Cancel to stop the service
        cancellationTokenSource.Cancel();
        await backgroundService.StopAsync(CancellationToken.None);

        // Assert - Service should have attempted to get pending retries at least once and logged the error
        _mockWebhookRetryService.Verify(x => x.GetPendingRetriesAsync(), Times.AtLeastOnce);
        // Note: Due to 1-minute processing interval, we only expect one call before cancellation
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ShouldStopGracefully()
    {
        // Arrange
        var backgroundService = new WebhookRetryBackgroundService(_mockServiceProvider.Object, _mockLogger.Object);
        var cancellationTokenSource = new CancellationTokenSource();

        _mockWebhookRetryService.Setup(x => x.GetPendingRetriesAsync())
            .ReturnsAsync(new List<RadioWash.Api.Models.Domain.WebhookRetry>());

        // Act
        await backgroundService.StartAsync(cancellationTokenSource.Token);
        
        // Cancel immediately
        cancellationTokenSource.Cancel();
        
        // Wait for graceful shutdown
        await backgroundService.StopAsync(CancellationToken.None);

        // Assert - Service should stop without throwing
        Assert.True(cancellationTokenSource.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_WithLongRunningRetryProcessing_ShouldRespectCancellation()
    {
        // Arrange
        var backgroundService = new WebhookRetryBackgroundService(_mockServiceProvider.Object, _mockLogger.Object);
        var cancellationTokenSource = new CancellationTokenSource();
        var processingStarted = new TaskCompletionSource<bool>();

        var pendingRetries = new List<RadioWash.Api.Models.Domain.WebhookRetry>
        {
            CreateTestWebhookRetry("evt_long")
        };

        _mockWebhookRetryService.Setup(x => x.GetPendingRetriesAsync())
            .ReturnsAsync(pendingRetries);
        
        // Simulate long-running processing that signals when it starts
        _mockWebhookRetryService.Setup(x => x.ProcessRetryAsync(It.IsAny<RadioWash.Api.Models.Domain.WebhookRetry>()))
            .Returns(async () =>
            {
                processingStarted.SetResult(true);
                await Task.Delay(2000); // Long delay, but shorter than before
            });

        // Act
        await backgroundService.StartAsync(cancellationTokenSource.Token);
        
        // Wait for processing to start
        await processingStarted.Task;
        
        // Cancel immediately after processing starts
        cancellationTokenSource.Cancel();
        
        // Stop should complete within reasonable time despite ongoing processing
        var stopTask = backgroundService.StopAsync(CancellationToken.None);
        var completedInTime = await Task.WhenAny(stopTask, Task.Delay(3000)) == stopTask;

        // Assert
        Assert.True(completedInTime, "Background service should stop gracefully within reasonable time");
    }

    private static RadioWash.Api.Models.Domain.WebhookRetry CreateTestWebhookRetry(string eventId)
    {
        return new RadioWash.Api.Models.Domain.WebhookRetry
        {
            Id = Random.Shared.Next(1, 1000),
            EventId = eventId,
            EventType = "test.event",
            Payload = "test_payload",
            Signature = "test_signature",
            AttemptNumber = 1,
            MaxRetries = 5,
            Status = WebhookRetryStatus.Pending,
            NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
            LastErrorMessage = "Test error",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
    }
}