using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Tests.Unit.Services;
using Stripe;

namespace RadioWash.Api.Tests.Unit.Services.WebhookRetry;

public class StripeWebhookProcessorTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ISubscriptionService> _mockSubscriptionService;
    private readonly Mock<CustomerService> _mockCustomerService;
    private readonly Mock<Stripe.SubscriptionService> _mockStripeSubscriptionService;
    private readonly Mock<ILogger<StripeWebhookProcessor>> _mockLogger;
    private readonly StripeWebhookProcessor _processor;

    public StripeWebhookProcessorTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockSubscriptionService = new Mock<ISubscriptionService>();
        _mockCustomerService = new Mock<CustomerService>();
        _mockStripeSubscriptionService = new Mock<Stripe.SubscriptionService>();
        _mockLogger = new Mock<ILogger<StripeWebhookProcessor>>();

        _mockConfiguration.Setup(x => x["Stripe:SecretKey"]).Returns("sk_test_123");

        _processor = new StripeWebhookProcessor(
            _mockConfiguration.Object,
            _mockSubscriptionService.Object,
            _mockCustomerService.Object,
            _mockStripeSubscriptionService.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Deserializes a payload the way the retry pipeline does: no signature involved.
    /// </summary>
    private static Event ParseEvent(string payloadJson) =>
        EventUtility.ParseEvent(payloadJson, throwOnApiVersionMismatch: false);

    #region Subscription Created / Updated Tests

    [Theory]
    [InlineData("created")]
    [InlineData("updated")]
    public async Task ProcessEventAsync_WithSubscriptionChangedEvent_ShouldSyncCurrentStateFromStripe(string change)
    {
        // Arrange - the event's embedded subscription is only a pointer; the state written
        // must come from a fresh Stripe fetch, same as the invoice handlers.
        var subscriptionId = "sub_123";
        var payload = change == "created"
            ? StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(subscriptionId, "cus_123", "price_123", 17)
            : StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook(subscriptionId, "active");
        var stripeEvent = ParseEvent(payload);
        var currentStripeState = new Stripe.Subscription { Id = subscriptionId, CustomerId = "cus_123", Status = "active" };

        _mockStripeSubscriptionService
            .Setup(x => x.GetAsync(subscriptionId, It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentStripeState);
        _mockSubscriptionService
            .Setup(x => x.SyncFromStripeAsync(currentStripeState, It.IsAny<Func<Task<int?>>?>()))
            .ReturnsAsync(CreateUserSubscription());

        // Act
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert - synced from the fetched state, not the event snapshot
        _mockStripeSubscriptionService.Verify(x => x.GetAsync(subscriptionId,
            It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockSubscriptionService.Verify(x => x.SyncFromStripeAsync(
            currentStripeState, It.IsAny<Func<Task<int?>>?>()), Times.Once);
    }

    [Fact]
    public async Task ProcessEventAsync_WithStaleActiveSnapshot_ShouldNotResurrectCanceledSubscription()
    {
        // Arrange - Stripe can redeliver a failed `updated` event for up to 3 days. Its
        // embedded snapshot says "active", but the subscription has since been canceled;
        // applying the snapshot would re-entitle a canceled user for free. The current
        // (canceled) Stripe state must be what gets synced.
        var subscriptionId = "sub_since_canceled";
        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook(subscriptionId, "active");
        var stripeEvent = ParseEvent(payload);
        var currentStripeState = new Stripe.Subscription { Id = subscriptionId, Status = "canceled" };

        _mockStripeSubscriptionService
            .Setup(x => x.GetAsync(subscriptionId, It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentStripeState);
        _mockSubscriptionService
            .Setup(x => x.SyncFromStripeAsync(currentStripeState, It.IsAny<Func<Task<int?>>?>()))
            .ReturnsAsync(CreateUserSubscription());

        // Act
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert - only the fetched canceled state is synced; the stale active snapshot never is
        _mockSubscriptionService.Verify(x => x.SyncFromStripeAsync(
            It.Is<Stripe.Subscription>(s => s.Status == "canceled"), It.IsAny<Func<Task<int?>>?>()), Times.Once);
        _mockSubscriptionService.Verify(x => x.SyncFromStripeAsync(
            It.Is<Stripe.Subscription>(s => s.Status == "active"), It.IsAny<Func<Task<int?>>?>()), Times.Never);
    }

    [Fact]
    public async Task ProcessEventAsync_WithSubscriptionUpdatedAndNullDataObject_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var stripeEvent = new Event
        {
            Id = "evt_null_object",
            Type = "customer.subscription.updated",
            Data = new EventData { Object = null! }
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _processor.ProcessEventAsync(stripeEvent));
    }

    [Fact]
    public async Task ProcessEventAsync_WithSubscriptionCreated_FallbackShouldResolveUserIdFromCustomerMetadata()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var customerId = "cus_123";
        var userId = 17;
        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(subscriptionId, customerId, "price_123", userId);
        var stripeEvent = ParseEvent(payload);

        _mockStripeSubscriptionService
            .Setup(x => x.GetAsync(subscriptionId, It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Stripe.Subscription { Id = subscriptionId, CustomerId = customerId, Status = "active" });

        Func<Task<int?>>? capturedFallback = null;
        _mockSubscriptionService
            .Setup(x => x.SyncFromStripeAsync(It.IsAny<Stripe.Subscription>(), It.IsAny<Func<Task<int?>>?>()))
            .Callback<Stripe.Subscription, Func<Task<int?>>?>((_, fallback) => capturedFallback = fallback)
            .ReturnsAsync(CreateUserSubscription());

        _mockCustomerService
            .Setup(x => x.GetAsync(customerId, It.IsAny<CustomerGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer
            {
                Id = customerId,
                Metadata = new Dictionary<string, string> { { "userId", userId.ToString() } }
            });

        // Act
        await _processor.ProcessEventAsync(stripeEvent);
        var resolvedUserId = await capturedFallback!.Invoke();

        // Assert
        Assert.Equal(userId, resolvedUserId);
        _mockCustomerService.Verify(x => x.GetAsync(customerId,
            It.IsAny<CustomerGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessEventAsync_WhenSyncFromStripeThrows_ShouldPropagate()
    {
        // Arrange
        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook("sub_123", "active");
        var stripeEvent = ParseEvent(payload);

        _mockStripeSubscriptionService
            .Setup(x => x.GetAsync("sub_123", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Stripe.Subscription { Id = "sub_123", Status = "active" });
        _mockSubscriptionService
            .Setup(x => x.SyncFromStripeAsync(It.IsAny<Stripe.Subscription>(), It.IsAny<Func<Task<int?>>?>()))
            .ThrowsAsync(new InvalidOperationException("sync failed"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _processor.ProcessEventAsync(stripeEvent));
        Assert.Equal("sync failed", ex.Message);
    }

    #endregion

    #region Subscription Deleted Tests

    [Fact]
    public async Task ProcessEventAsync_WithSubscriptionDeletedAndLocalRecord_ShouldCancelSubscription()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionDeletedWebhook(subscriptionId);
        var stripeEvent = ParseEvent(payload);

        _mockSubscriptionService.Setup(x => x.GetByStripeSubscriptionIdAsync(subscriptionId))
            .ReturnsAsync(CreateUserSubscription());
        _mockSubscriptionService.Setup(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "canceled"))
            .ReturnsAsync(CreateUserSubscription());

        // Act
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(subscriptionId, "canceled"), Times.Once);
    }

    [Fact]
    public async Task ProcessEventAsync_WithSubscriptionDeletedAndNoLocalRecord_ShouldNotUpdateStatus()
    {
        // Arrange
        var subscriptionId = "sub_missing";
        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionDeletedWebhook(subscriptionId);
        var stripeEvent = ParseEvent(payload);

        _mockSubscriptionService.Setup(x => x.GetByStripeSubscriptionIdAsync(subscriptionId))
            .ReturnsAsync((UserSubscription?)null);

        // Act (no throw expected)
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Invoice Payment Tests

    [Theory]
    [InlineData("invoice.payment_failed")]
    [InlineData("invoice.payment_succeeded")]
    public async Task ProcessEventAsync_WithInvoiceEvent_ShouldSyncCurrentStateFromStripe(string eventType)
    {
        // Arrange - invoice events only carry a subscription pointer; the processor must
        // fetch the subscription's CURRENT state from Stripe rather than deriving a status
        // from the event type (a delayed redelivery of payment_succeeded must not resurrect
        // a since-canceled subscription).
        var subscriptionId = "sub_123";
        var payload = eventType == "invoice.payment_failed"
            ? StripeWebhookPayloadBuilder.CreateInvoicePaymentFailedWebhook("in_123", subscriptionId)
            : StripeWebhookPayloadBuilder.CreateInvoicePaymentSucceededWebhook("in_123", subscriptionId);
        var stripeEvent = ParseEvent(payload);
        var currentStripeState = new Stripe.Subscription { Id = subscriptionId, Status = "past_due" };

        _mockStripeSubscriptionService
            .Setup(x => x.GetAsync(subscriptionId, It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentStripeState);

        // Act
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert
        _mockSubscriptionService.Verify(
            x => x.SyncFromStripeAsync(currentStripeState, It.IsAny<Func<Task<int?>>?>()), Times.Once);
        _mockSubscriptionService.Verify(
            x => x.UpdateSubscriptionStatusAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("invoice.payment_failed")]
    [InlineData("invoice.payment_succeeded")]
    public async Task ProcessEventAsync_WithInvoiceEventAndNoSubscriptionReference_ShouldNotTouchSubscriptions(string eventType)
    {
        // Arrange - Invoice with no subscription key at all (not subscription-related).
        var payload = CreateInvoiceWebhookWithoutSubscriptionKey("in_123", eventType);
        var stripeEvent = ParseEvent(payload);

        // Act (no throw expected)
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert
        _mockSubscriptionService.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("invoice.payment_failed")]
    [InlineData("invoice.payment_succeeded")]
    public async Task ProcessEventAsync_WithInvoiceEventAndExplicitNullSubscription_ShouldNotTouchSubscriptions(string eventType)
    {
        // Arrange - real non-subscription invoices carry an explicit "subscription": null,
        // which must be treated the same as a missing key.
        var payload = eventType == "invoice.payment_failed"
            ? StripeWebhookPayloadBuilder.CreateInvoicePaymentFailedWebhook("in_123", null)
            : StripeWebhookPayloadBuilder.CreateInvoicePaymentSucceededWebhook("in_123", null);
        var stripeEvent = ParseEvent(payload);

        // Act (no throw expected)
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert
        _mockSubscriptionService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessEventAsync_WithInvoiceEventAndNoLocalRecord_ShouldFetchFromStripeAndSync()
    {
        // Arrange
        var subscriptionId = "sub_not_local";
        var payload = StripeWebhookPayloadBuilder.CreateInvoicePaymentSucceededWebhook("in_123", subscriptionId);
        var stripeEvent = ParseEvent(payload);

        var fetchedSubscription = new Stripe.Subscription { Id = subscriptionId, CustomerId = "cus_123" };

        _mockSubscriptionService.Setup(x => x.GetByStripeSubscriptionIdAsync(subscriptionId))
            .ReturnsAsync((UserSubscription?)null);
        _mockStripeSubscriptionService
            .Setup(x => x.GetAsync(subscriptionId, It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetchedSubscription);
        _mockSubscriptionService
            .Setup(x => x.SyncFromStripeAsync(fetchedSubscription, It.IsAny<Func<Task<int?>>?>()))
            .ReturnsAsync(CreateUserSubscription());

        // Act
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert
        _mockStripeSubscriptionService.Verify(x => x.GetAsync(subscriptionId,
            It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockSubscriptionService.Verify(x => x.SyncFromStripeAsync(fetchedSubscription, It.IsAny<Func<Task<int?>>?>()), Times.Once);
        _mockSubscriptionService.Verify(x => x.UpdateSubscriptionStatusAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Checkout Session Completed Tests

    [Fact]
    public async Task ProcessEventAsync_WithCheckoutCompleted_ShouldNotCallSubscriptionService()
    {
        // Arrange
        var payload = StripeWebhookPayloadBuilder.CreateCheckoutSessionCompletedWebhook("cs_123", 1);
        var stripeEvent = ParseEvent(payload);

        // Act (no throw expected)
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert
        _mockSubscriptionService.VerifyNoOtherCalls();
    }

    #endregion

    #region Unhandled Event Tests

    [Fact]
    public async Task ProcessEventAsync_WithUnhandledEventType_ShouldNotThrow()
    {
        // Arrange
        var stripeEvent = new Event { Id = "evt_unhandled", Type = "customer.created" };

        // Act (no throw expected)
        await _processor.ProcessEventAsync(stripeEvent);

        // Assert
        _mockSubscriptionService.VerifyNoOtherCalls();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Builds an invoice event payload whose invoice object omits the "subscription" key
    /// entirely. StripeWebhookPayloadBuilder's overloads always serialize the key (as JSON
    /// null when no id is given), and GetSubscriptionIdFromInvoice throws on an explicit
    /// null token — a known implementation gap.
    /// </summary>
    private static string CreateInvoiceWebhookWithoutSubscriptionKey(string invoiceId, string eventType)
    {
        var payload = new
        {
            id = "evt_123",
            @object = "event",
            api_version = "2020-08-27",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new
            {
                @object = new
                {
                    id = invoiceId,
                    @object = "invoice"
                }
            },
            livemode = false,
            pending_webhooks = 1,
            request = new
            {
                id = "req_123",
                idempotency_key = (string?)null
            },
            type = eventType
        };

        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private static UserSubscription CreateUserSubscription()
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

    #endregion
}
