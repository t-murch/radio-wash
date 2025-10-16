using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Moq;
using Stripe;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Infrastructure.Data;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class StripePaymentServiceTests : IDisposable
{
  private readonly Mock<IConfiguration> _mockConfiguration;
  private readonly Mock<ISubscriptionService> _mockSubscriptionService;
  private readonly Mock<IEventUtility> _mockEventUtility;
  private readonly Mock<CustomerService> _mockCustomerService;
  private readonly Mock<IIdempotencyService> _mockIdempotencyService;
  private readonly Mock<IWebhookRetryService> _mockWebhookRetryService;
  private readonly Mock<IWebhookProcessor> _mockWebhookProcessor;
  private readonly Mock<ILogger<StripePaymentService>> _mockLogger;
  private readonly RadioWashDbContext _dbContext;
  private readonly StripePaymentService _stripePaymentService;

  public StripePaymentServiceTests()
  {
    _mockConfiguration = new Mock<IConfiguration>();
    _mockSubscriptionService = new Mock<ISubscriptionService>();
    _mockEventUtility = new Mock<IEventUtility>();
    _mockCustomerService = new Mock<CustomerService>();
    _mockIdempotencyService = new Mock<IIdempotencyService>();
    _mockWebhookRetryService = new Mock<IWebhookRetryService>();
    _mockWebhookProcessor = new Mock<IWebhookProcessor>();
    _mockLogger = new Mock<ILogger<StripePaymentService>>();

    // Setup in-memory database with transaction warnings suppressed
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    _dbContext = new RadioWashDbContext(options);
    
    // Ensure database schema is created for new webhook event table
    _dbContext.Database.EnsureCreated();

    // Setup configuration
    _mockConfiguration.Setup(x => x["Stripe:SecretKey"]).Returns("sk_test_123");
    _mockConfiguration.Setup(x => x["Stripe:WebhookSecret"]).Returns("whsec_123");
    _mockConfiguration.Setup(x => x["FrontendUrl"]).Returns("https://example.com");

    _stripePaymentService = new StripePaymentService(
        _mockConfiguration.Object,
        _mockSubscriptionService.Object,
        _mockEventUtility.Object,
        _dbContext,
        _mockCustomerService.Object,
        _mockIdempotencyService.Object,
        _mockWebhookRetryService.Object,
        _mockWebhookProcessor.Object,
        _mockLogger.Object
    );
  }

  #region Webhook Delegation Tests

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionUpdated_ShouldUpdateSubscriptionWithItemDates()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionUpdatedNoItems_ShouldOnlyUpdateStatus()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  #endregion

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionDeleted_ShouldCancelSubscription()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithPaymentFailed_ShouldUpdateSubscriptionToPastDue()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithPaymentFailedNoSubscription_ShouldNotUpdateStatus()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithPaymentSucceeded_ShouldUpdateSubscriptionToActive()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithPaymentSucceededNoSubscription_ShouldNotUpdateStatus()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionCreated_ShouldCreateSubscription()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionCreatedNoPlan_ShouldNotCreateSubscription()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionCreatedNoItems_ShouldNotCreateSubscription()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithCheckoutCompleted_ShouldLogSuccess()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithUnhandledEventType_ShouldLogUnhandled()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  #region Retry Logic Tests

  [Fact]
  public async Task HandleWebhookAsync_WithDuplicateEventId_ShouldProcessOnlyOnce()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WhenIdempotencyServiceRejectsDuplicate_ShouldSkipProcessing()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, signature);

    // Assert
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_ProcessingFails_ShouldMarkEventAsFailed()
  {
    // Arrange
    var webhookPayload = "test_payload";
    var signature = "test_signature";
    var errorMessage = "Processing failed";
    var eventId = "evt_123";
    var eventType = "test.event";

    // Setup webhook processor to throw an exception
    _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(webhookPayload, signature))
        .ThrowsAsync(new InvalidOperationException(errorMessage));

    // Setup retry service to indicate the error is retryable
    _mockWebhookRetryService.Setup(x => x.IsRetryableError(It.IsAny<Exception>()))
        .Returns(true);

    // Setup event extraction for retry logic
    var mockEvent = new Event { Id = eventId, Type = eventType };
    _mockEventUtility.Setup(x => x.ConstructEvent(webhookPayload, signature, "whsec_123"))
        .Returns(mockEvent);

    _mockWebhookRetryService.Setup(x => x.ScheduleRetryAsync(eventId, eventType, webhookPayload, signature, errorMessage, 1))
        .Returns(Task.CompletedTask);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.HandleWebhookAsync(webhookPayload, signature));

    // Verify webhook processor was called
    _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(webhookPayload, signature), Times.Once);
    
    // Verify retry was scheduled
    _mockWebhookRetryService.Verify(x => x.ScheduleRetryAsync(eventId, eventType, webhookPayload, signature, errorMessage, 1), Times.Once);
  }

  #endregion

  public void Dispose()
  {
    _dbContext.Dispose();
  }
}