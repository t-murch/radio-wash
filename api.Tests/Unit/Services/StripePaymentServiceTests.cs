using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Moq;
using Stripe;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Infrastructure.Data;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class StripePaymentServiceTests : IDisposable
{
  private readonly Mock<IConfiguration> _mockConfiguration;
  private readonly Mock<ISubscriptionService> _mockSubscriptionService;
  private readonly Mock<IEventUtility> _mockEventUtility;
  private readonly Mock<CustomerService> _mockCustomerService;
  private readonly Mock<ILogger<StripePaymentService>> _mockLogger;
  private readonly RadioWashDbContext _dbContext;
  private readonly StripePaymentService _stripePaymentService;

  public StripePaymentServiceTests()
  {
    _mockConfiguration = new Mock<IConfiguration>();
    _mockSubscriptionService = new Mock<ISubscriptionService>();
    _mockEventUtility = new Mock<IEventUtility>();
    _mockCustomerService = new Mock<CustomerService>();
    _mockLogger = new Mock<ILogger<StripePaymentService>>();

    // Setup in-memory database
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;
    _dbContext = new RadioWashDbContext(options);

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
        _mockLogger.Object
    );
  }

  #region Subscription Updated Tests

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionUpdated_ShouldUpdateSubscriptionWithItemDates()
  {
    // Arrange
    var subscriptionId = "sub_123";
    var periodStart = DateTime.UtcNow.AddDays(-30);
    var periodEnd = DateTime.UtcNow.AddDays(30);
    var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook(
        subscriptionId, "active", periodStart, periodEnd);

    SetupEventUtilityMock(webhookPayload, "customer.subscription.updated", subscriptionId);
    _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"))
        .ReturnsAsync(CreateMockUserSubscription());
    _mockSubscriptionService.Setup(x => x.UpdateSubscriptionDatesAsync(subscriptionId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
        .ReturnsAsync(CreateMockUserSubscription());

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"), Times.Once);
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionDatesAsync(
        subscriptionId, 
        It.Is<DateTime>(d => Math.Abs((d - periodStart).TotalSeconds) < 1),
        It.Is<DateTime>(d => Math.Abs((d - periodEnd).TotalSeconds) < 1)), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionUpdatedNoItems_ShouldOnlyUpdateStatus()
  {
    // Arrange
    var subscriptionId = "sub_123";
    var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook(subscriptionId, "active", null, null);

    SetupEventUtilityMockWithEmptyItems(webhookPayload, "customer.subscription.updated", subscriptionId);
    _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"))
        .ReturnsAsync(CreateMockUserSubscription());

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"), Times.Once);
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionDatesAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
  }

  #endregion

  #region Subscription Deleted Tests

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionDeleted_ShouldCancelSubscription()
  {
    // Arrange
    var subscriptionId = "sub_123";
    var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionDeletedWebhook(subscriptionId);

    SetupEventUtilityMock(webhookPayload, "customer.subscription.deleted", subscriptionId);
    _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "canceled"))
        .ReturnsAsync(CreateMockUserSubscription());

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "canceled"), Times.Once);
  }

  #endregion

  #region Payment Failed Tests

  [Fact]
  public async Task HandleWebhookAsync_WithPaymentFailed_ShouldUpdateSubscriptionToPastDue()
  {
    // Arrange
    var subscriptionId = "sub_123";
    var invoiceId = "in_123";
    var webhookPayload = StripeWebhookPayloadBuilder.CreateInvoicePaymentFailedWebhook(invoiceId, subscriptionId);

    SetupEventUtilityMockForInvoice(webhookPayload, "invoice.payment_failed", invoiceId, subscriptionId);
    _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "past_due"))
        .ReturnsAsync(CreateMockUserSubscription());

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "past_due"), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithPaymentFailedNoSubscription_ShouldNotUpdateStatus()
  {
    // Arrange
    var invoiceId = "in_123";
    var webhookPayload = StripeWebhookPayloadBuilder.CreateInvoicePaymentFailedWebhook(invoiceId, null);

    SetupEventUtilityMockForInvoice(webhookPayload, "invoice.payment_failed", invoiceId, null);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
  }

  #endregion

  #region Payment Succeeded Tests

  [Fact]
  public async Task HandleWebhookAsync_WithPaymentSucceeded_ShouldUpdateSubscriptionToActive()
  {
    // Arrange
    var subscriptionId = "sub_123";
    var invoiceId = "in_123";
    var webhookPayload = StripeWebhookPayloadBuilder.CreateInvoicePaymentSucceededWebhook(invoiceId, subscriptionId);

    SetupEventUtilityMockForInvoice(webhookPayload, "invoice.payment_succeeded", invoiceId, subscriptionId);
    _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"))
        .ReturnsAsync(CreateMockUserSubscription());

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithPaymentSucceededNoSubscription_ShouldNotUpdateStatus()
  {
    // Arrange
    var invoiceId = "in_123";
    var webhookPayload = StripeWebhookPayloadBuilder.CreateInvoicePaymentSucceededWebhook(invoiceId, null);

    SetupEventUtilityMockForInvoice(webhookPayload, "invoice.payment_succeeded", invoiceId, null);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
  }

  #endregion

  #region Subscription Created Tests

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionCreated_ShouldCreateSubscription()
  {
    // Arrange
    var subscriptionId = "sub_123";
    var customerId = "cus_123";
    var priceId = "price_123";
    var userId = 17;
    var planId = 1;
    var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(
        subscriptionId, customerId, priceId, userId);

    var mockPlan = CreateMockSubscriptionPlan(planId, priceId);
    SetupEventUtilityMockForSubscriptionCreated(webhookPayload, subscriptionId, customerId, priceId, userId);
    _mockSubscriptionService.Setup(x => x.GetPlanByStripePriceIdAsync(priceId))
        .ReturnsAsync(mockPlan);
    _mockSubscriptionService.Setup(x => x.CreateSubscriptionAsync(userId, planId, subscriptionId, customerId))
        .ReturnsAsync(CreateMockUserSubscription());

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.GetPlanByStripePriceIdAsync(priceId), Times.Once);
    _mockSubscriptionService.Verify(x => x.CreateSubscriptionAsync(userId, planId, subscriptionId, customerId), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionCreatedNoPlan_ShouldNotCreateSubscription()
  {
    // Arrange
    var subscriptionId = "sub_123";
    var customerId = "cus_123";
    var priceId = "price_unknown";
    var userId = 17;
    var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(
        subscriptionId, customerId, priceId, userId);

    SetupEventUtilityMockForSubscriptionCreated(webhookPayload, subscriptionId, customerId, priceId, userId);
    _mockSubscriptionService.Setup(x => x.GetPlanByStripePriceIdAsync(priceId))
        .ReturnsAsync((SubscriptionPlan?)null);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.GetPlanByStripePriceIdAsync(priceId), Times.Once);
    _mockSubscriptionService.Verify(x => x.CreateSubscriptionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithSubscriptionCreatedNoItems_ShouldNotCreateSubscription()
  {
    // Arrange
    var subscriptionId = "sub_123";
    var customerId = "cus_123";
    var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhookNoItems(subscriptionId, customerId);

    SetupEventUtilityMockForSubscriptionCreatedNoItems(webhookPayload, subscriptionId, customerId);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert
    _mockSubscriptionService.Verify(x => x.GetPlanByStripePriceIdAsync(It.IsAny<string>()), Times.Never);
    _mockSubscriptionService.Verify(x => x.CreateSubscriptionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
  }

  #endregion

  #region Checkout Session Completed Tests

  [Fact]
  public async Task HandleWebhookAsync_WithCheckoutCompleted_ShouldLogSuccess()
  {
    // Arrange
    var sessionId = "cs_123";
    var userId = 1;
    var webhookPayload = StripeWebhookPayloadBuilder.CreateCheckoutSessionCompletedWebhook(sessionId, userId);

    SetupEventUtilityMockForCheckoutCompleted(webhookPayload, sessionId, userId);

    // Act
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert - This method currently only logs, so we verify it doesn't throw
    // In a more comprehensive test, we'd verify the logging output
  }

  #endregion

  #region Unhandled Event Tests

  [Fact]
  public async Task HandleWebhookAsync_WithUnhandledEventType_ShouldLogUnhandled()
  {
    // Arrange
    var eventType = "customer.created";
    var mockEvent = new Event { Id = "evt_test_" + Guid.NewGuid().ToString()[..8], Type = eventType };
    
    _mockEventUtility.Setup(x => x.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
        .Returns(mockEvent);

    // Act
    await _stripePaymentService.HandleWebhookAsync("{}", "test_signature");

    // Assert - Should not throw and should log unhandled event
    // In a comprehensive test, we'd verify the log output
  }

  #endregion

  #region Helper Methods

  private void SetupEventUtilityMock(string payload, string eventType, string subscriptionId)
  {
    var subscription = new Stripe.Subscription { Id = subscriptionId, Status = "active" };
    
    // Create subscription items with period dates
    var subscriptionItem = new SubscriptionItem();
    var periodStart = DateTime.UtcNow.AddDays(-30);
    var periodEnd = DateTime.UtcNow.AddDays(30);
    subscriptionItem.CurrentPeriodStart = periodStart;
    subscriptionItem.CurrentPeriodEnd = periodEnd;
    
    subscription.Items = new StripeList<SubscriptionItem>
    {
      Data = new List<SubscriptionItem> { subscriptionItem }
    };

    var mockEvent = new Event
    {
      Id = "evt_test_" + Guid.NewGuid().ToString()[..8],
      Type = eventType,
      Data = new EventData { Object = subscription }
    };

    _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
        .Returns(mockEvent);
  }

  private void SetupEventUtilityMockWithEmptyItems(string payload, string eventType, string subscriptionId)
  {
    var subscription = new Stripe.Subscription { Id = subscriptionId, Status = "active", Items = null };
    var mockEvent = new Event
    {
      Id = "evt_test_" + Guid.NewGuid().ToString()[..8],
      Type = eventType,
      Data = new EventData { Object = subscription }
    };

    _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
        .Returns(mockEvent);
  }

  private void SetupEventUtilityMockForInvoice(string payload, string eventType, string invoiceId, string? subscriptionId)
  {
    var invoice = new Invoice { Id = invoiceId };
    
    // Mock RawJObject for subscription reference
    var rawJObject = new Newtonsoft.Json.Linq.JObject();
    if (!string.IsNullOrEmpty(subscriptionId))
    {
      rawJObject["subscription"] = subscriptionId;
    }

    // Minimal reflection usage for setting RawJObject - unavoidable due to Stripe SDK design
    var rawJObjectProperty = typeof(StripeEntity).GetProperty("RawJObject", 
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
    rawJObjectProperty?.SetValue(invoice, rawJObject);

    var mockEvent = new Event
    {
      Id = "evt_test_" + Guid.NewGuid().ToString()[..8],
      Type = eventType,
      Data = new EventData { Object = invoice }
    };

    _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
        .Returns(mockEvent);
  }

  private void SetupEventUtilityMockForSubscriptionCreated(string payload, string subscriptionId, string customerId, string priceId, int userId)
  {
    var subscription = new Stripe.Subscription 
    { 
      Id = subscriptionId, 
      CustomerId = customerId,
      Status = "active"
    };

    // Create subscription items with price ID
    var subscriptionItem = new SubscriptionItem();
    var price = new Price { Id = priceId };
    subscriptionItem.Price = price;
    
    subscription.Items = new StripeList<SubscriptionItem>
    {
      Data = new List<SubscriptionItem> { subscriptionItem }
    };

    // Set subscription metadata with user ID
    subscription.Metadata = new Dictionary<string, string>
    {
      { "userId", userId.ToString() }
    };

    var mockEvent = new Event
    {
      Id = "evt_test_" + Guid.NewGuid().ToString()[..8],
      Type = "customer.subscription.created",
      Data = new EventData { Object = subscription }
    };

    _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
        .Returns(mockEvent);
  }

  private void SetupEventUtilityMockForSubscriptionCreatedNoItems(string payload, string subscriptionId, string customerId)
  {
    var subscription = new Stripe.Subscription 
    { 
      Id = subscriptionId, 
      CustomerId = customerId,
      Items = null // No items
    };

    var mockEvent = new Event
    {
      Id = "evt_test_" + Guid.NewGuid().ToString()[..8],
      Type = "customer.subscription.created",
      Data = new EventData { Object = subscription }
    };

    _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
        .Returns(mockEvent);
  }

  private void SetupEventUtilityMockForCheckoutCompleted(string payload, string sessionId, int userId)
  {
    var session = new Stripe.Checkout.Session 
    { 
      Id = sessionId,
      Metadata = new Dictionary<string, string>
      {
        { "userId", userId.ToString() }
      }
    };

    var mockEvent = new Event
    {
      Id = "evt_test_" + Guid.NewGuid().ToString()[..8],
      Type = "checkout.session.completed",
      Data = new EventData { Object = session }
    };

    _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
        .Returns(mockEvent);
  }

  private static UserSubscription CreateMockUserSubscription()
  {
    return new UserSubscription
    {
      Id = 1,
      UserId = 1,
      PlanId = 1,
      StripeSubscriptionId = "sub_123",
      StripeCustomerId = "cus_123",
      Status = "active",
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  private static SubscriptionPlan CreateMockSubscriptionPlan(int planId, string stripePriceId)
  {
    return new SubscriptionPlan
    {
      Id = planId,
      Name = "Test Plan",
      PriceInCents = 500,
      BillingPeriod = "monthly",
      StripePriceId = stripePriceId,
      IsActive = true,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  #endregion

  #region Race Condition and Idempotency Tests

  [Fact]
  public async Task HandleWebhookAsync_WithDuplicateEventId_ShouldProcessOnlyOnce()
  {
    // Arrange
    var eventId = "evt_test_duplicate";
    var subscriptionId = "sub_123";
    var webhookPayload = "test_payload";

    // Setup the first event
    var mockEvent = new Event
    {
      Id = eventId,
      Type = "customer.subscription.updated",
      Data = new EventData 
      { 
        Object = new Stripe.Subscription 
        { 
          Id = subscriptionId, 
          Status = "active" 
        } 
      }
    };

    _mockEventUtility.Setup(x => x.ConstructEvent(webhookPayload, "test_signature", "whsec_123"))
        .Returns(mockEvent);
    _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"))
        .ReturnsAsync(CreateMockUserSubscription());

    // Act - Process the webhook twice
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");
    await _stripePaymentService.HandleWebhookAsync(webhookPayload, "test_signature");

    // Assert - Subscription service should only be called once
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"), Times.Once);

    // Verify that webhook event was recorded in database
    var processedEvent = await _dbContext.ProcessedWebhookEvents
        .FirstOrDefaultAsync(e => e.EventId == eventId);
    Assert.NotNull(processedEvent);
    Assert.True(processedEvent.IsSuccessful);
  }

  [Fact]
  public async Task HandleWebhookAsync_ConcurrentRequests_ShouldHandleRaceConditionGracefully()
  {
    // Arrange
    var eventId = "evt_test_concurrent";
    var subscriptionId = "sub_concurrent";
    var webhookPayload = "concurrent_payload";

    var mockEvent = new Event
    {
      Id = eventId,
      Type = "customer.subscription.updated",
      Data = new EventData 
      { 
        Object = new Stripe.Subscription 
        { 
          Id = subscriptionId, 
          Status = "active" 
        } 
      }
    };

    _mockEventUtility.Setup(x => x.ConstructEvent(webhookPayload, "concurrent_signature", "whsec_123"))
        .Returns(mockEvent);
    _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"))
        .ReturnsAsync(CreateMockUserSubscription());

    // Act - Simulate concurrent processing using tasks
    var task1 = _stripePaymentService.HandleWebhookAsync(webhookPayload, "concurrent_signature");
    var task2 = _stripePaymentService.HandleWebhookAsync(webhookPayload, "concurrent_signature");

    await Task.WhenAll(task1, task2);

    // Assert - Subscription service should only be called once despite concurrent requests
    _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"), Times.Once);

    // Verify only one processed webhook event exists in database
    var processedEvents = await _dbContext.ProcessedWebhookEvents
        .Where(e => e.EventId == eventId)
        .ToListAsync();
    Assert.Single(processedEvents);
    Assert.True(processedEvents.First().IsSuccessful);
  }

  [Fact]
  public async Task HandleWebhookAsync_ProcessingFails_ShouldMarkEventAsFailed()
  {
    // Arrange
    var eventId = "evt_test_failed";
    var subscriptionId = "sub_failed";
    var webhookPayload = "failed_payload";

    var mockEvent = new Event
    {
      Id = eventId,
      Type = "customer.subscription.updated",
      Data = new EventData 
      { 
        Object = new Stripe.Subscription 
        { 
          Id = subscriptionId, 
          Status = "active" 
        } 
      }
    };

    _mockEventUtility.Setup(x => x.ConstructEvent(webhookPayload, "failed_signature", "whsec_123"))
        .Returns(mockEvent);
    _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"))
        .ThrowsAsync(new InvalidOperationException("Database error"));

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.HandleWebhookAsync(webhookPayload, "failed_signature"));

    // Verify that failed webhook event was recorded in database
    var processedEvent = await _dbContext.ProcessedWebhookEvents
        .FirstOrDefaultAsync(e => e.EventId == eventId);
    Assert.NotNull(processedEvent);
    Assert.False(processedEvent.IsSuccessful);
    Assert.Equal("Database error", processedEvent.ErrorMessage);
  }

  #endregion

  public void Dispose()
  {
    _dbContext.Dispose();
  }
}