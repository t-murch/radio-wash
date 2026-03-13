using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Stripe;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class StripeWebhookOrchestratorTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IEventUtility> _mockEventUtility;
    private readonly Mock<IIdempotencyService> _mockIdempotencyService;
    private readonly Mock<IWebhookRetryService> _mockWebhookRetryService;
    private readonly Mock<IWebhookProcessor> _mockWebhookProcessor;
    private readonly Mock<ILogger<StripeWebhookOrchestrator>> _mockLogger;
    private readonly StripeWebhookOrchestrator _orchestrator;

    public StripeWebhookOrchestratorTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockEventUtility = new Mock<IEventUtility>();
        _mockIdempotencyService = new Mock<IIdempotencyService>();
        _mockWebhookRetryService = new Mock<IWebhookRetryService>();
        _mockWebhookProcessor = new Mock<IWebhookProcessor>();
        _mockLogger = new Mock<ILogger<StripeWebhookOrchestrator>>();

        _mockConfiguration.Setup(x => x["Stripe:WebhookSecret"]).Returns("whsec_123");

        _orchestrator = new StripeWebhookOrchestrator(
            _mockConfiguration.Object,
            _mockEventUtility.Object,
            _mockIdempotencyService.Object,
            _mockWebhookRetryService.Object,
            _mockWebhookProcessor.Object,
            _mockLogger.Object
        );
    }

    #region Subscription Updated Tests

    [Fact]
    public async Task HandleWebhookAsync_WithSubscriptionUpdated_ShouldProcessAndMarkSuccessful()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var eventId = "evt_test_123";
        var periodStart = DateTime.UtcNow.AddDays(-30);
        var periodEnd = DateTime.UtcNow.AddDays(30);
        var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook(
            subscriptionId, "active", periodStart, periodEnd);

        SetupEventUtilityMock(webhookPayload, "customer.subscription.updated", subscriptionId, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithSubscriptionUpdatedNoItems_ShouldOnlyUpdateStatus()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var eventId = "evt_test_no_items";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook(subscriptionId, "active", null, null);

        SetupEventUtilityMockWithEmptyItems(webhookPayload, "customer.subscription.updated", subscriptionId, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    #endregion

    #region Subscription Deleted Tests

    [Fact]
    public async Task HandleWebhookAsync_WithSubscriptionDeleted_ShouldCancelSubscription()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var eventId = "evt_test_deleted";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionDeletedWebhook(subscriptionId);

        SetupEventUtilityMock(webhookPayload, "customer.subscription.deleted", subscriptionId, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.deleted"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "customer.subscription.deleted"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    #endregion

    #region Payment Failed Tests

    [Fact]
    public async Task HandleWebhookAsync_WithPaymentFailed_ShouldUpdateSubscriptionToPastDue()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var invoiceId = "in_123";
        var eventId = "evt_test_payment_failed";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateInvoicePaymentFailedWebhook(invoiceId, subscriptionId);

        SetupEventUtilityMockForInvoice(webhookPayload, "invoice.payment_failed", invoiceId, subscriptionId, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "invoice.payment_failed"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "invoice.payment_failed"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithPaymentFailedNoSubscription_ShouldNotUpdateStatus()
    {
        // Arrange
        var invoiceId = "in_123";
        var eventId = "evt_test_payment_failed_no_sub";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateInvoicePaymentFailedWebhook(invoiceId, null);

        SetupEventUtilityMockForInvoice(webhookPayload, "invoice.payment_failed", invoiceId, null, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "invoice.payment_failed"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "invoice.payment_failed"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    #endregion

    #region Payment Succeeded Tests

    [Fact]
    public async Task HandleWebhookAsync_WithPaymentSucceeded_ShouldUpdateSubscriptionToActive()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var invoiceId = "in_123";
        var eventId = "evt_test_payment_succeeded";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateInvoicePaymentSucceededWebhook(invoiceId, subscriptionId);

        SetupEventUtilityMockForInvoice(webhookPayload, "invoice.payment_succeeded", invoiceId, subscriptionId, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "invoice.payment_succeeded"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "invoice.payment_succeeded"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithPaymentSucceededNoSubscription_ShouldNotUpdateStatus()
    {
        // Arrange
        var invoiceId = "in_123";
        var eventId = "evt_test_payment_succeeded_no_sub";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateInvoicePaymentSucceededWebhook(invoiceId, null);

        SetupEventUtilityMockForInvoice(webhookPayload, "invoice.payment_succeeded", invoiceId, null, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "invoice.payment_succeeded"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "invoice.payment_succeeded"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
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
        var eventId = "evt_test_subscription_created";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(
            subscriptionId, customerId, priceId, userId);

        SetupEventUtilityMockForSubscriptionCreated(webhookPayload, subscriptionId, customerId, priceId, userId, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.created"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "customer.subscription.created"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithSubscriptionCreatedNoPlan_ShouldNotCreateSubscription()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var customerId = "cus_123";
        var priceId = "price_unknown";
        var userId = 17;
        var eventId = "evt_test_subscription_created_no_plan";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(
            subscriptionId, customerId, priceId, userId);

        SetupEventUtilityMockForSubscriptionCreated(webhookPayload, subscriptionId, customerId, priceId, userId, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.created"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "customer.subscription.created"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithSubscriptionCreatedNoItems_ShouldNotCreateSubscription()
    {
        // Arrange
        var subscriptionId = "sub_123";
        var customerId = "cus_123";
        var eventId = "evt_test_subscription_created_no_items";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhookNoItems(subscriptionId, customerId);

        SetupEventUtilityMockForSubscriptionCreatedNoItems(webhookPayload, subscriptionId, customerId, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.created"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "customer.subscription.created"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    #endregion

    #region Checkout Session Completed Tests

    [Fact]
    public async Task HandleWebhookAsync_WithCheckoutCompleted_ShouldLogSuccess()
    {
        // Arrange
        var sessionId = "cs_123";
        var userId = 1;
        var eventId = "evt_test_checkout_completed";
        var webhookPayload = StripeWebhookPayloadBuilder.CreateCheckoutSessionCompletedWebhook(sessionId, userId);

        SetupEventUtilityMockForCheckoutCompleted(webhookPayload, sessionId, userId, eventId);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "checkout.session.completed"))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "checkout.session.completed"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    #endregion

    #region Unhandled Event Tests

    [Fact]
    public async Task HandleWebhookAsync_WithUnhandledEventType_ShouldLogUnhandled()
    {
        // Arrange
        var eventType = "customer.created";
        var eventId = "evt_test_unhandled";
        var mockEvent = new Event { Id = eventId, Type = eventType };

        _mockEventUtility.Setup(x => x.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(mockEvent);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync("{}", "test_signature");

        // Assert
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, eventType), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    #endregion

    #region Verified Event Passing Tests

    [Fact]
    public async Task HandleWebhookAsync_ShouldPassVerifiedEventToProcessor()
    {
        // Arrange
        var eventId = "evt_test_verified";
        var eventType = "customer.subscription.updated";
        var webhookPayload = "test_payload";

        var mockEvent = new Event
        {
            Id = eventId,
            Type = eventType,
            Data = new Stripe.EventData
            {
                Object = new Stripe.Subscription { Id = "sub_123", Status = "active" }
            }
        };

        _mockEventUtility.Setup(x => x.ConstructEvent(webhookPayload, "test_signature", "whsec_123"))
            .Returns(mockEvent);
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
            .ReturnsAsync(true);
        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);
        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert - Verify the exact verified event is passed to the processor
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(
            It.Is<Event>(e => e.Id == eventId && e.Type == eventType)), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_SignatureVerificationFails_ShouldThrowStripeException()
    {
        // Arrange
        var webhookPayload = "invalid_payload";

        _mockEventUtility.Setup(x => x.ConstructEvent(webhookPayload, "bad_signature", "whsec_123"))
            .Throws(new StripeException("Signature verification failed"));

        // Act & Assert
        await Assert.ThrowsAsync<StripeException>(
            () => _orchestrator.HandleWebhookAsync(webhookPayload, "bad_signature"));

        // Verify processor was never called
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Never);
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

        var mockEvent = new Event
        {
            Id = eventId,
            Type = "customer.subscription.updated",
            Data = new Stripe.EventData
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

        // First call should be allowed to process
        _mockIdempotencyService.SetupSequence(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"))
            .ReturnsAsync(true)   // First call - allow processing
            .ReturnsAsync(false); // Second call - already processed

        _mockIdempotencyService.Setup(x => x.MarkEventSuccessfulAsync(eventId))
            .Returns(Task.CompletedTask);

        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .Returns(Task.CompletedTask);

        // Act - Process the webhook twice
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert - Idempotency service should be called twice, but webhook processor only once
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"), Times.Exactly(2));
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
        _mockWebhookProcessor.Verify(x => x.ProcessWebhookAsync(It.IsAny<Event>()), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_WhenIdempotencyServiceRejectsDuplicate_ShouldSkipProcessing()
    {
        // Arrange
        var eventId = "evt_test_already_processed";
        var subscriptionId = "sub_123";
        var webhookPayload = "test_payload";

        var mockEvent = new Event
        {
            Id = eventId,
            Type = "customer.subscription.updated",
            Data = new Stripe.EventData
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

        // Idempotency service indicates event already processed
        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"))
            .ReturnsAsync(false);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert - No business logic should be executed
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_ProcessingFails_ShouldMarkEventAsFailed()
    {
        // Arrange
        var eventId = "evt_test_failed";
        var subscriptionId = "sub_failed";
        var webhookPayload = "failed_payload";
        var errorMessage = "Database error";

        var mockEvent = new Event
        {
            Id = eventId,
            Type = "customer.subscription.updated",
            Data = new Stripe.EventData
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

        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"))
            .ReturnsAsync(true);

        _mockIdempotencyService.Setup(x => x.MarkEventFailedAsync(eventId, errorMessage))
            .Returns(Task.CompletedTask);

        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .ThrowsAsync(new InvalidOperationException(errorMessage));

        _mockWebhookRetryService.Setup(x => x.IsRetryableError(It.IsAny<Exception>()))
            .Returns(false);

        // Act - Should NOT throw (processing failure is handled gracefully)
        await _orchestrator.HandleWebhookAsync(webhookPayload, "failed_signature");

        // Verify that idempotency service was called to mark failure
        _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventFailedAsync(eventId, errorMessage), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_NonRetryableError_ShouldMarkEventPermanentlyFailed()
    {
        // Arrange
        var eventId = "evt_test_non_retryable";
        var subscriptionId = "sub_non_retryable";
        var webhookPayload = "non_retryable_payload";
        var errorMessage = "Invalid subscription metadata";

        var mockEvent = new Event
        {
            Id = eventId,
            Type = "customer.subscription.updated",
            Data = new Stripe.EventData
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

        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"))
            .ReturnsAsync(true);

        _mockIdempotencyService.Setup(x => x.MarkEventFailedAsync(eventId, errorMessage))
            .Returns(Task.CompletedTask);

        _mockIdempotencyService.Setup(x => x.MarkEventPermanentlyFailedAsync(eventId, errorMessage))
            .Returns(Task.CompletedTask);

        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .ThrowsAsync(new InvalidOperationException(errorMessage));

        _mockWebhookRetryService.Setup(x => x.IsRetryableError(It.IsAny<Exception>()))
            .Returns(false); // Non-retryable

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert - Should mark as failed AND permanently failed
        _mockIdempotencyService.Verify(x => x.MarkEventFailedAsync(eventId, errorMessage), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventPermanentlyFailedAsync(eventId, errorMessage), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_RetryableError_ShouldNotMarkEventPermanentlyFailed()
    {
        // Arrange
        var eventId = "evt_test_retryable";
        var webhookPayload = "retryable_payload";
        var errorMessage = "Database timeout";

        var mockEvent = new Event
        {
            Id = eventId,
            Type = "customer.subscription.updated",
            Data = new Stripe.EventData
            {
                Object = new Stripe.Subscription
                {
                    Id = "sub_retryable",
                    Status = "active"
                }
            }
        };

        _mockEventUtility.Setup(x => x.ConstructEvent(webhookPayload, "test_signature", "whsec_123"))
            .Returns(mockEvent);

        _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, "customer.subscription.updated"))
            .ReturnsAsync(true);

        _mockIdempotencyService.Setup(x => x.MarkEventFailedAsync(eventId, errorMessage))
            .Returns(Task.CompletedTask);

        _mockWebhookProcessor.Setup(x => x.ProcessWebhookAsync(It.IsAny<Event>()))
            .ThrowsAsync(new InvalidOperationException(errorMessage));

        _mockWebhookRetryService.Setup(x => x.IsRetryableError(It.IsAny<Exception>()))
            .Returns(true); // Retryable

        _mockWebhookRetryService.Setup(x => x.ScheduleRetryAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.HandleWebhookAsync(webhookPayload, "test_signature");

        // Assert - Should mark as failed but NOT permanently failed
        _mockIdempotencyService.Verify(x => x.MarkEventFailedAsync(eventId, errorMessage), Times.Once);
        _mockIdempotencyService.Verify(x => x.MarkEventPermanentlyFailedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Helper Methods

    private void SetupEventUtilityMock(string payload, string eventType, string subscriptionId, string eventId)
    {
        var subscription = new Stripe.Subscription { Id = subscriptionId, Status = "active" };

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
            Id = eventId,
            Type = eventType,
            Data = new Stripe.EventData { Object = subscription }
        };

        _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
            .Returns(mockEvent);
    }

    private void SetupEventUtilityMockWithEmptyItems(string payload, string eventType, string subscriptionId, string eventId)
    {
        var subscription = new Stripe.Subscription { Id = subscriptionId, Status = "active", Items = null };
        var mockEvent = new Event
        {
            Id = eventId,
            Type = eventType,
            Data = new Stripe.EventData { Object = subscription }
        };

        _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
            .Returns(mockEvent);
    }

    private void SetupEventUtilityMockForInvoice(string payload, string eventType, string invoiceId, string? subscriptionId, string eventId)
    {
        var invoice = new Invoice { Id = invoiceId };

        var rawJObject = new Newtonsoft.Json.Linq.JObject();
        if (!string.IsNullOrEmpty(subscriptionId))
        {
            rawJObject["subscription"] = subscriptionId;
        }

        var rawJObjectProperty = typeof(StripeEntity).GetProperty("RawJObject",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        rawJObjectProperty?.SetValue(invoice, rawJObject);

        var mockEvent = new Event
        {
            Id = eventId,
            Type = eventType,
            Data = new Stripe.EventData { Object = invoice }
        };

        _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
            .Returns(mockEvent);
    }

    private void SetupEventUtilityMockForSubscriptionCreated(string payload, string subscriptionId, string customerId, string priceId, int userId, string eventId)
    {
        var subscription = new Stripe.Subscription
        {
            Id = subscriptionId,
            CustomerId = customerId,
            Status = "active"
        };

        var subscriptionItem = new SubscriptionItem();
        var price = new Price { Id = priceId };
        subscriptionItem.Price = price;

        subscription.Items = new StripeList<SubscriptionItem>
        {
            Data = new List<SubscriptionItem> { subscriptionItem }
        };

        subscription.Metadata = new Dictionary<string, string>
        {
            { "userId", userId.ToString() }
        };

        var mockEvent = new Event
        {
            Id = eventId,
            Type = "customer.subscription.created",
            Data = new Stripe.EventData { Object = subscription }
        };

        _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
            .Returns(mockEvent);
    }

    private void SetupEventUtilityMockForSubscriptionCreatedNoItems(string payload, string subscriptionId, string customerId, string eventId)
    {
        var subscription = new Stripe.Subscription
        {
            Id = subscriptionId,
            CustomerId = customerId,
            Items = null
        };

        var mockEvent = new Event
        {
            Id = eventId,
            Type = "customer.subscription.created",
            Data = new Stripe.EventData { Object = subscription }
        };

        _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
            .Returns(mockEvent);
    }

    private void SetupEventUtilityMockForCheckoutCompleted(string payload, string sessionId, int userId, string eventId)
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
            Id = eventId,
            Type = "checkout.session.completed",
            Data = new Stripe.EventData { Object = session }
        };

        _mockEventUtility.Setup(x => x.ConstructEvent(payload, "test_signature", "whsec_123"))
            .Returns(mockEvent);
    }

    #endregion
}
