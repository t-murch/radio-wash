using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Tests.Unit.Services.WebhookRetry;

public class StripeWebhookProcessorTests : IDisposable
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ISubscriptionService> _mockSubscriptionService;
    private readonly Mock<IEventUtility> _mockEventUtility;
    private readonly Mock<CustomerService> _mockCustomerService;
    private readonly Mock<ILogger<StripeWebhookProcessor>> _mockLogger;
    private readonly RadioWashDbContext _dbContext;
    private readonly StripeWebhookProcessor _stripeWebhookProcessor;

    public StripeWebhookProcessorTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockSubscriptionService = new Mock<ISubscriptionService>();
        _mockEventUtility = new Mock<IEventUtility>();
        _mockCustomerService = new Mock<CustomerService>();
        _mockLogger = new Mock<ILogger<StripeWebhookProcessor>>();

        // Setup in-memory database
        var options = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new RadioWashDbContext(options);
        _dbContext.Database.EnsureCreated();

        // Setup configuration
        _mockConfiguration.Setup(x => x["Stripe:SecretKey"]).Returns("sk_test_123");
        _mockConfiguration.Setup(x => x["Stripe:WebhookSecret"]).Returns("whsec_123");

        _stripeWebhookProcessor = new StripeWebhookProcessor(
            _mockConfiguration.Object,
            _mockSubscriptionService.Object,
            _mockEventUtility.Object,
            _dbContext,
            _mockCustomerService.Object,
            _mockLogger.Object);
    }

    #region Configuration Tests

    [Fact]
    public async Task ProcessWebhookAsync_WithMissingWebhookSecret_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _mockConfiguration.Setup(x => x["Stripe:WebhookSecret"]).Returns((string?)null);
        
        var processor = new StripeWebhookProcessor(
            _mockConfiguration.Object,
            _mockSubscriptionService.Object,
            _mockEventUtility.Object,
            _dbContext,
            _mockCustomerService.Object,
            _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessWebhookAsync("payload", "signature"));
    }

    #endregion

    #region Subscription Updated Tests

    [Fact]
    public async Task ProcessWebhookAsync_WithSubscriptionUpdated_ShouldUpdateSubscriptionWithDates()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var eventId = "evt_test_123";
        var periodStart = DateTime.UtcNow.AddDays(-30);
        var periodEnd = DateTime.UtcNow.AddDays(30);

        var subscription = CreateMockSubscription(subscriptionId, "active", periodStart, periodEnd);
        var mockEvent = CreateMockEvent(eventId, "customer.subscription.updated", subscription);

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);
        _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"))
            .ReturnsAsync(CreateMockUserSubscription());
        _mockSubscriptionService.Setup(x => x.UpdateSubscriptionDatesAsync(subscriptionId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(CreateMockUserSubscription());

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"), Times.Once);
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionDatesAsync(
            subscriptionId,
            It.Is<DateTime>(d => Math.Abs((d - periodStart).TotalSeconds) < 1),
            It.Is<DateTime>(d => Math.Abs((d - periodEnd).TotalSeconds) < 1)), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookAsync_WithSubscriptionUpdatedNoItems_ShouldOnlyUpdateStatus()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var eventId = "evt_test_no_items";

        var subscription = new Stripe.Subscription { Id = subscriptionId, Status = "active", Items = null };
        var mockEvent = CreateMockEvent(eventId, "customer.subscription.updated", subscription);

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);
        _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"))
            .ReturnsAsync(CreateMockUserSubscription());

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"), Times.Once);
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionDatesAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
    }

    #endregion

    #region Subscription Deleted Tests

    [Fact]
    public async Task ProcessWebhookAsync_WithSubscriptionDeleted_ShouldCancelSubscription()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var eventId = "evt_test_deleted";

        var subscription = new Stripe.Subscription { Id = subscriptionId };
        var mockEvent = CreateMockEvent(eventId, "customer.subscription.deleted", subscription);

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);
        _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "canceled"))
            .ReturnsAsync(CreateMockUserSubscription());

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "canceled"), Times.Once);
    }

    #endregion

    #region Payment Failed Tests

    [Fact]
    public async Task ProcessWebhookAsync_WithPaymentFailed_ShouldUpdateSubscriptionToPastDue()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var invoiceId = "in_123";
        var eventId = "evt_test_payment_failed";

        var invoice = CreateMockInvoice(invoiceId, subscriptionId);
        var mockEvent = CreateMockEvent(eventId, "invoice.payment_failed", invoice);

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);
        _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "past_due"))
            .ReturnsAsync(CreateMockUserSubscription());

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "past_due"), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookAsync_WithPaymentFailedNoSubscription_ShouldNotUpdateStatus()
    {
        // Arrange
        var invoiceId = "in_123";
        var eventId = "evt_test_payment_failed_no_sub";

        var invoice = CreateMockInvoice(invoiceId, null);
        var mockEvent = CreateMockEvent(eventId, "invoice.payment_failed", invoice);

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Payment Succeeded Tests

    [Fact]
    public async Task ProcessWebhookAsync_WithPaymentSucceeded_ShouldUpdateSubscriptionToActive()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var invoiceId = "in_123";
        var eventId = "evt_test_payment_succeeded";

        var invoice = CreateMockInvoice(invoiceId, subscriptionId);
        var mockEvent = CreateMockEvent(eventId, "invoice.payment_succeeded", invoice);

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);
        _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"))
            .ReturnsAsync(CreateMockUserSubscription());

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "active"), Times.Once);
    }

    #endregion

    #region Subscription Created Tests

    [Fact]
    public async Task ProcessWebhookAsync_WithSubscriptionCreated_ShouldCreateSubscription()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var customerId = "cus_123";
        var priceId = "price_123";
        var userId = 17;
        var planId = 1;
        var eventId = "evt_test_subscription_created";

        var subscription = CreateMockSubscriptionCreated(subscriptionId, customerId, priceId, userId);
        var mockEvent = CreateMockEvent(eventId, "customer.subscription.created", subscription);
        var mockPlan = CreateMockSubscriptionPlan(planId, priceId);

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);
        _mockSubscriptionService.Setup(x => x.GetPlanByStripePriceIdAsync(priceId))
            .ReturnsAsync(mockPlan);
        _mockSubscriptionService.Setup(x => x.CreateSubscriptionAsync(userId, planId, subscriptionId, customerId))
            .ReturnsAsync(CreateMockUserSubscription());

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert
        _mockSubscriptionService.Verify(x => x.GetPlanByStripePriceIdAsync(priceId), Times.Once);
        _mockSubscriptionService.Verify(x => x.CreateSubscriptionAsync(userId, planId, subscriptionId, customerId), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookAsync_WithSubscriptionCreatedNoPlan_ShouldNotCreateSubscription()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var customerId = "cus_123";
        var priceId = "price_unknown";
        var userId = 17;
        var eventId = "evt_test_subscription_created_no_plan";

        var subscription = CreateMockSubscriptionCreated(subscriptionId, customerId, priceId, userId);
        var mockEvent = CreateMockEvent(eventId, "customer.subscription.created", subscription);

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);
        _mockSubscriptionService.Setup(x => x.GetPlanByStripePriceIdAsync(priceId))
            .ReturnsAsync((SubscriptionPlan?)null);

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert
        _mockSubscriptionService.Verify(x => x.GetPlanByStripePriceIdAsync(priceId), Times.Once);
        _mockSubscriptionService.Verify(x => x.CreateSubscriptionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Checkout Session Completed Tests

    [Fact]
    public async Task ProcessWebhookAsync_WithCheckoutCompleted_ShouldProcessSuccessfully()
    {
        // Arrange
        var sessionId = "cs_123";
        var userId = 1;
        var eventId = "evt_test_checkout_completed";

        var session = new Stripe.Checkout.Session
        {
            Id = sessionId,
            Metadata = new Dictionary<string, string> { { "userId", userId.ToString() } }
        };
        var mockEvent = CreateMockEvent(eventId, "checkout.session.completed", session);

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert - Should complete without error
        Assert.True(true); // Placeholder assertion - main test is that no exception is thrown
    }

    #endregion

    #region Unhandled Event Tests

    [Fact]
    public async Task ProcessWebhookAsync_WithUnhandledEventType_ShouldProcessWithoutError()
    {
        // Arrange
        var eventId = "evt_test_unhandled";
        var mockEvent = new Event { Id = eventId, Type = "customer.created" };

        _mockEventUtility.Setup(x => x.ConstructEvent("payload", "signature", "whsec_123"))
            .Returns(mockEvent);

        // Act
        await _stripeWebhookProcessor.ProcessWebhookAsync("payload", "signature");

        // Assert - Should complete without error
        Assert.True(true); // Placeholder assertion - main test is that no exception is thrown
    }

    #endregion

    #region Helper Methods

    private static Event CreateMockEvent(string eventId, string eventType, object dataObject)
    {
        return new Event
        {
            Id = eventId,
            Type = eventType,
            Data = new Stripe.EventData { Object = dataObject as Stripe.IHasObject }
        };
    }

    private static Stripe.Subscription CreateMockSubscription(string subscriptionId, string status, DateTime? periodStart = null, DateTime? periodEnd = null)
    {
        var subscription = new Stripe.Subscription { Id = subscriptionId, Status = status };

        if (periodStart.HasValue && periodEnd.HasValue)
        {
            var subscriptionItem = new SubscriptionItem
            {
                CurrentPeriodStart = periodStart.Value,
                CurrentPeriodEnd = periodEnd.Value
            };

            subscription.Items = new StripeList<SubscriptionItem>
            {
                Data = new List<SubscriptionItem> { subscriptionItem }
            };
        }

        return subscription;
    }

    private static Invoice CreateMockInvoice(string invoiceId, string? subscriptionId)
    {
        var invoice = new Invoice { Id = invoiceId };

        if (!string.IsNullOrEmpty(subscriptionId))
        {
            var rawJObject = new Newtonsoft.Json.Linq.JObject
            {
                ["subscription"] = subscriptionId
            };

            var rawJObjectProperty = typeof(StripeEntity).GetProperty("RawJObject",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            rawJObjectProperty?.SetValue(invoice, rawJObject);
        }

        return invoice;
    }

    private static Stripe.Subscription CreateMockSubscriptionCreated(string subscriptionId, string customerId, string priceId, int userId)
    {
        var subscription = new Stripe.Subscription
        {
            Id = subscriptionId,
            CustomerId = customerId,
            Status = "active",
            Metadata = new Dictionary<string, string> { { "userId", userId.ToString() } }
        };

        var subscriptionItem = new SubscriptionItem
        {
            Price = new Price { Id = priceId }
        };

        subscription.Items = new StripeList<SubscriptionItem>
        {
            Data = new List<SubscriptionItem> { subscriptionItem }
        };

        return subscription;
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

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}