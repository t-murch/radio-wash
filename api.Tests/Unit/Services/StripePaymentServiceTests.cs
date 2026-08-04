using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Stripe;
using RadioWash.Api.Services.Exceptions;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Tests HandleWebhookAsync orchestration (signature verification, idempotency claiming,
/// processor dispatch, success/failure marking, retry scheduling) plus the Stripe client
/// calls behind checkout, portal, cancel-at-period-end, and session retrieval. Per-event-type
/// dispatch behavior is covered by the StripeWebhookProcessor tests.
/// </summary>
public class StripePaymentServiceTests
{
  private const string Payload = "{\"id\":\"evt_test\"}";
  private const string Signature = "test_signature";
  private const string WebhookSecret = "whsec_123";
  private const string FrontendUrl = "https://example.com";

  private readonly Mock<IConfiguration> _mockConfiguration;
  private readonly Mock<IEventUtility> _mockEventUtility;
  private readonly Mock<IIdempotencyService> _mockIdempotencyService;
  private readonly Mock<IWebhookRetryService> _mockWebhookRetryService;
  private readonly Mock<IWebhookProcessor> _mockWebhookProcessor;
  private readonly Mock<Stripe.Checkout.SessionService> _mockCheckoutSessionService;
  private readonly Mock<Stripe.BillingPortal.SessionService> _mockPortalSessionService;
  private readonly Mock<Stripe.SubscriptionService> _mockStripeSubscriptionService;
  private readonly Mock<ILogger<StripePaymentService>> _mockLogger;
  private readonly StripePaymentService _stripePaymentService;

  public StripePaymentServiceTests()
  {
    _mockConfiguration = new Mock<IConfiguration>();
    _mockEventUtility = new Mock<IEventUtility>();
    _mockIdempotencyService = new Mock<IIdempotencyService>();
    _mockWebhookRetryService = new Mock<IWebhookRetryService>();
    _mockWebhookProcessor = new Mock<IWebhookProcessor>();
    _mockCheckoutSessionService = new Mock<Stripe.Checkout.SessionService>();
    _mockPortalSessionService = new Mock<Stripe.BillingPortal.SessionService>();
    _mockStripeSubscriptionService = new Mock<Stripe.SubscriptionService>();
    _mockLogger = new Mock<ILogger<StripePaymentService>>();

    _mockConfiguration.Setup(x => x["Stripe:SecretKey"]).Returns("sk_test_123");
    _mockConfiguration.Setup(x => x["Stripe:WebhookSecret"]).Returns(WebhookSecret);
    _mockConfiguration.Setup(x => x["FrontendUrl"]).Returns(FrontendUrl);

    _stripePaymentService = new StripePaymentService(
        _mockConfiguration.Object,
        _mockEventUtility.Object,
        _mockIdempotencyService.Object,
        _mockWebhookRetryService.Object,
        _mockWebhookProcessor.Object,
        _mockCheckoutSessionService.Object,
        _mockPortalSessionService.Object,
        _mockStripeSubscriptionService.Object,
        _mockLogger.Object
    );
  }

  private Event SetupVerifiedEvent(string eventId, string eventType)
  {
    var stripeEvent = new Event { Id = eventId, Type = eventType };
    _mockEventUtility.Setup(x => x.ConstructEvent(Payload, Signature, WebhookSecret))
        .Returns(stripeEvent);
    return stripeEvent;
  }

  #region Successful Processing

  [Fact]
  public async Task HandleWebhookAsync_WithValidEvent_ProcessesAndMarksSuccessful()
  {
    // Arrange
    var eventId = "evt_test_success";
    var eventType = "customer.subscription.updated";
    var stripeEvent = SetupVerifiedEvent(eventId, eventType);

    _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
        .ReturnsAsync(true);
    _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(stripeEvent))
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(Payload, Signature);

    // Assert
    _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, eventType), Times.Once);
    _mockWebhookProcessor.Verify(x => x.ProcessEventAsync(stripeEvent), Times.Once);
    _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
    _mockIdempotencyService.Verify(x => x.MarkEventFailedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task HandleWebhookAsync_PassesVerifiedEventToProcessor()
  {
    // Arrange
    var eventId = "evt_test_dispatch";
    var eventType = "invoice.payment_succeeded";
    SetupVerifiedEvent(eventId, eventType);

    _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
        .ReturnsAsync(true);

    Event? receivedEvent = null;
    _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(It.IsAny<Event>()))
        .Callback<Event>(e => receivedEvent = e)
        .Returns(Task.CompletedTask);

    // Act
    await _stripePaymentService.HandleWebhookAsync(Payload, Signature);

    // Assert - the processor receives the event constructed from the verified payload
    Assert.NotNull(receivedEvent);
    Assert.Equal(eventId, receivedEvent!.Id);
    Assert.Equal(eventType, receivedEvent.Type);
  }

  #endregion

  #region Idempotency

  [Fact]
  public async Task HandleWebhookAsync_WhenIdempotencyClaimDenied_SkipsProcessing()
  {
    // Arrange
    var eventId = "evt_test_already_processed";
    var eventType = "customer.subscription.updated";
    SetupVerifiedEvent(eventId, eventType);

    _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
        .ReturnsAsync(false);

    // Act - no exception expected
    await _stripePaymentService.HandleWebhookAsync(Payload, Signature);

    // Assert - nothing was processed or marked
    _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, eventType), Times.Once);
    _mockWebhookProcessor.Verify(x => x.ProcessEventAsync(It.IsAny<Event>()), Times.Never);
    _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(It.IsAny<string>()), Times.Never);
    _mockIdempotencyService.Verify(x => x.MarkEventFailedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithDuplicateEventId_ProcessesOnlyOnce()
  {
    // Arrange
    var eventId = "evt_test_duplicate";
    var eventType = "customer.subscription.updated";
    SetupVerifiedEvent(eventId, eventType);

    _mockIdempotencyService.SetupSequence(x => x.TryProcessEventAsync(eventId, eventType))
        .ReturnsAsync(true)   // First delivery - claim granted
        .ReturnsAsync(false); // Second delivery - already processed

    _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(It.IsAny<Event>()))
        .Returns(Task.CompletedTask);

    // Act - process the same webhook twice
    await _stripePaymentService.HandleWebhookAsync(Payload, Signature);
    await _stripePaymentService.HandleWebhookAsync(Payload, Signature);

    // Assert - claim attempted twice, but processed and marked only once
    _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(eventId, eventType), Times.Exactly(2));
    _mockWebhookProcessor.Verify(x => x.ProcessEventAsync(It.IsAny<Event>()), Times.Once);
    _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(eventId), Times.Once);
  }

  #endregion

  #region Processing Failures

  [Fact]
  public async Task HandleWebhookAsync_WhenProcessorFails_MarksFailedAndRethrows()
  {
    // Arrange
    var eventId = "evt_test_failed";
    var eventType = "customer.subscription.updated";
    var errorMessage = "Database error";
    SetupVerifiedEvent(eventId, eventType);

    _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
        .ReturnsAsync(true);
    _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(It.IsAny<Event>()))
        .ThrowsAsync(new InvalidOperationException(errorMessage));
    _mockWebhookRetryService.Setup(x => x.IsRetryableError(It.IsAny<Exception>()))
        .Returns(false);

    // Act & Assert - the original exception propagates, NOT wrapped in
    // WebhookSignatureVerificationException (which would turn a transient
    // failure into a permanent 400 rejection)
    var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.HandleWebhookAsync(Payload, Signature));
    Assert.Equal(errorMessage, thrown.Message);

    _mockIdempotencyService.Verify(x => x.MarkEventFailedAsync(eventId, errorMessage), Times.Once);
    _mockIdempotencyService.Verify(x => x.MarkEventSuccessfulAsync(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task HandleWebhookAsync_WhenProcessorThrowsStripeException_RethrowsUnwrapped()
  {
    // Arrange - a StripeException thrown DURING processing (e.g. an API call inside a
    // handler) must not be mistaken for a signature verification failure
    var eventId = "evt_test_stripe_error";
    var eventType = "invoice.payment_failed";
    SetupVerifiedEvent(eventId, eventType);

    _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
        .ReturnsAsync(true);
    _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(It.IsAny<Event>()))
        .ThrowsAsync(new StripeException("Stripe API unavailable"));
    _mockWebhookRetryService.Setup(x => x.IsRetryableError(It.IsAny<Exception>()))
        .Returns(true);

    // Act & Assert - rethrown as StripeException, not WebhookSignatureVerificationException
    await Assert.ThrowsAsync<StripeException>(
        () => _stripePaymentService.HandleWebhookAsync(Payload, Signature));

    _mockIdempotencyService.Verify(x => x.MarkEventFailedAsync(eventId, "Stripe API unavailable"), Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithRetryableProcessingError_SchedulesRetry()
  {
    // Arrange
    var eventId = "evt_test_retryable";
    var eventType = "customer.subscription.updated";
    var errorMessage = "Transient database timeout";
    SetupVerifiedEvent(eventId, eventType);

    _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
        .ReturnsAsync(true);
    _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(It.IsAny<Event>()))
        .ThrowsAsync(new InvalidOperationException(errorMessage));
    _mockWebhookRetryService.Setup(x => x.IsRetryableError(It.IsAny<Exception>()))
        .Returns(true);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.HandleWebhookAsync(Payload, Signature));

    _mockWebhookRetryService.Verify(
        x => x.ScheduleRetryAsync(eventId, eventType, Payload, Signature, errorMessage, 1),
        Times.Once);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithNonRetryableProcessingError_DoesNotScheduleRetry()
  {
    // Arrange
    var eventId = "evt_test_non_retryable";
    var eventType = "customer.subscription.updated";
    SetupVerifiedEvent(eventId, eventType);

    _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
        .ReturnsAsync(true);
    _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(It.IsAny<Event>()))
        .ThrowsAsync(new InvalidOperationException("Permanent validation failure"));
    _mockWebhookRetryService.Setup(x => x.IsRetryableError(It.IsAny<Exception>()))
        .Returns(false);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.HandleWebhookAsync(Payload, Signature));

    _mockWebhookRetryService.Verify(
        x => x.ScheduleRetryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
        Times.Never);
  }

  [Fact]
  public async Task HandleWebhookAsync_WhenSchedulingRetryFails_StillRethrowsOriginalException()
  {
    // Arrange
    var eventId = "evt_test_retry_schedule_fails";
    var eventType = "customer.subscription.updated";
    var errorMessage = "Original processing error";
    SetupVerifiedEvent(eventId, eventType);

    _mockIdempotencyService.Setup(x => x.TryProcessEventAsync(eventId, eventType))
        .ReturnsAsync(true);
    _mockWebhookProcessor.Setup(x => x.ProcessEventAsync(It.IsAny<Event>()))
        .ThrowsAsync(new InvalidOperationException(errorMessage));
    _mockWebhookRetryService.Setup(x => x.IsRetryableError(It.IsAny<Exception>()))
        .Returns(true);
    _mockWebhookRetryService.Setup(x => x.ScheduleRetryAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
        .ThrowsAsync(new Exception("Retry scheduling failed"));

    // Act & Assert - the retry-scheduling failure is swallowed; the original exception wins
    var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.HandleWebhookAsync(Payload, Signature));
    Assert.Equal(errorMessage, thrown.Message);
  }

  #endregion

  #region Signature Verification and Configuration

  [Fact]
  public async Task HandleWebhookAsync_WhenSignatureVerificationFails_ThrowsWebhookSignatureVerificationException()
  {
    // Arrange
    var stripeException = new StripeException("Signature verification failed");
    _mockEventUtility.Setup(x => x.ConstructEvent(Payload, Signature, WebhookSecret))
        .Throws(stripeException);

    // Act & Assert
    var thrown = await Assert.ThrowsAsync<WebhookSignatureVerificationException>(
        () => _stripePaymentService.HandleWebhookAsync(Payload, Signature));
    Assert.Same(stripeException, thrown.InnerException);

    // Nothing downstream runs for an unauthenticated payload
    _mockIdempotencyService.Verify(x => x.TryProcessEventAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    _mockWebhookProcessor.Verify(x => x.ProcessEventAsync(It.IsAny<Event>()), Times.Never);
  }

  [Fact]
  public async Task HandleWebhookAsync_WithMissingWebhookSecret_ThrowsInvalidOperationException()
  {
    // Arrange
    _mockConfiguration.Setup(x => x["Stripe:WebhookSecret"]).Returns((string?)null);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.HandleWebhookAsync(Payload, Signature));

    _mockEventUtility.Verify(x => x.ConstructEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
  }

  #endregion

  #region CreateCheckoutSessionAsync

  [Fact]
  public async Task CreateCheckoutSessionAsync_BuildsSessionOptionsFromPlanAndUser()
  {
    // Arrange
    Stripe.Checkout.SessionCreateOptions? capturedOptions = null;
    _mockCheckoutSessionService
        .Setup(x => x.CreateAsync(
            It.IsAny<Stripe.Checkout.SessionCreateOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
        .Callback<Stripe.Checkout.SessionCreateOptions, RequestOptions, CancellationToken>(
            (options, _, _) => capturedOptions = options)
        .ReturnsAsync(new Stripe.Checkout.Session { Id = "cs_1", Url = "https://checkout.stripe.com/x" });

    // Act
    var url = await _stripePaymentService.CreateCheckoutSessionAsync(42, "price_abc");

    // Assert
    Assert.Equal("https://checkout.stripe.com/x", url);
    Assert.NotNull(capturedOptions);
    // Literal {CHECKOUT_SESSION_ID} placeholder is substituted by Stripe, not by us
    Assert.Equal($"{FrontendUrl}/subscription/success?session_id={{CHECKOUT_SESSION_ID}}", capturedOptions!.SuccessUrl);
    Assert.Equal($"{FrontendUrl}/subscription/cancel", capturedOptions.CancelUrl);
    Assert.Equal("subscription", capturedOptions.Mode);
    var lineItem = Assert.Single(capturedOptions.LineItems);
    Assert.Equal("price_abc", lineItem.Price);
    // userId metadata must be on both the session and the subscription it creates
    Assert.Equal("42", capturedOptions.Metadata["userId"]);
    Assert.Equal("42", capturedOptions.SubscriptionData.Metadata["userId"]);
  }

  [Fact]
  public async Task CreateCheckoutSessionAsync_WithClientRequestId_SendsIdempotencyKey()
  {
    // Arrange
    RequestOptions? capturedRequestOptions = null;
    _mockCheckoutSessionService
        .Setup(x => x.CreateAsync(
            It.IsAny<Stripe.Checkout.SessionCreateOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
        .Callback<Stripe.Checkout.SessionCreateOptions, RequestOptions, CancellationToken>(
            (_, requestOptions, _) => capturedRequestOptions = requestOptions)
        .ReturnsAsync(new Stripe.Checkout.Session { Id = "cs_1", Url = "https://checkout.stripe.com/x" });

    // Act
    await _stripePaymentService.CreateCheckoutSessionAsync(42, "price_abc", "abc");

    // Assert
    Assert.NotNull(capturedRequestOptions);
    Assert.Equal("checkout-42-abc", capturedRequestOptions!.IdempotencyKey);
  }

  [Fact]
  public async Task CreateCheckoutSessionAsync_WithoutClientRequestId_SendsNoRequestOptions()
  {
    // Arrange
    var requestOptionsCaptured = false;
    RequestOptions? capturedRequestOptions = null;
    _mockCheckoutSessionService
        .Setup(x => x.CreateAsync(
            It.IsAny<Stripe.Checkout.SessionCreateOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
        .Callback<Stripe.Checkout.SessionCreateOptions, RequestOptions, CancellationToken>(
            (_, requestOptions, _) => { requestOptionsCaptured = true; capturedRequestOptions = requestOptions; })
        .ReturnsAsync(new Stripe.Checkout.Session { Id = "cs_1", Url = "https://checkout.stripe.com/x" });

    // Act
    await _stripePaymentService.CreateCheckoutSessionAsync(42, "price_abc", clientRequestId: null);

    // Assert - no idempotency key means no RequestOptions at all
    Assert.True(requestOptionsCaptured);
    Assert.Null(capturedRequestOptions);
  }

  [Fact]
  public async Task CreateCheckoutSessionAsync_WhenSessionHasNoUrl_ThrowsInvalidOperationException()
  {
    // Arrange - a session without a redirect URL is unusable for the frontend
    _mockCheckoutSessionService
        .Setup(x => x.CreateAsync(
            It.IsAny<Stripe.Checkout.SessionCreateOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Stripe.Checkout.Session { Id = "cs_no_url", Url = null });

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.CreateCheckoutSessionAsync(42, "price_abc"));
    Assert.Contains("cs_no_url", exception.Message);
  }

  #endregion

  #region CancelAtPeriodEndAsync / GetCheckoutSessionAsync / CreatePortalSessionAsync

  [Fact]
  public async Task CancelAtPeriodEndAsync_UpdatesStripeSubscriptionWithCancelFlag()
  {
    // Arrange
    _mockStripeSubscriptionService
        .Setup(x => x.UpdateAsync(
            "sub_1",
            It.IsAny<SubscriptionUpdateOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Subscription { Id = "sub_1" });

    // Act
    await _stripePaymentService.CancelAtPeriodEndAsync("sub_1");

    // Assert
    _mockStripeSubscriptionService.Verify(x => x.UpdateAsync(
        "sub_1",
        It.Is<SubscriptionUpdateOptions>(o => o.CancelAtPeriodEnd == true),
        It.IsAny<RequestOptions>(),
        It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task GetCheckoutSessionAsync_RetrievesSessionWithSubscriptionExpanded()
  {
    // Arrange
    var session = new Stripe.Checkout.Session { Id = "cs_1" };
    _mockCheckoutSessionService
        .Setup(x => x.GetAsync(
            "cs_1",
            It.IsAny<Stripe.Checkout.SessionGetOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(session);

    // Act
    var result = await _stripePaymentService.GetCheckoutSessionAsync("cs_1");

    // Assert
    Assert.Same(session, result);
    _mockCheckoutSessionService.Verify(x => x.GetAsync(
        "cs_1",
        It.Is<Stripe.Checkout.SessionGetOptions>(o => o.Expand.Contains("subscription")),
        It.IsAny<RequestOptions>(),
        It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task CreatePortalSessionAsync_CreatesPortalSessionForCustomer()
  {
    // Arrange
    _mockPortalSessionService
        .Setup(x => x.CreateAsync(
            It.IsAny<Stripe.BillingPortal.SessionCreateOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Stripe.BillingPortal.Session { Url = "https://billing.stripe.com/p/session" });

    // Act
    var url = await _stripePaymentService.CreatePortalSessionAsync("cus_1");

    // Assert
    Assert.Equal("https://billing.stripe.com/p/session", url);
    _mockPortalSessionService.Verify(x => x.CreateAsync(
        It.Is<Stripe.BillingPortal.SessionCreateOptions>(o =>
            o.Customer == "cus_1" && o.ReturnUrl == $"{FrontendUrl}/dashboard"),
        It.IsAny<RequestOptions>(),
        It.IsAny<CancellationToken>()), Times.Once);
  }

  #endregion
}
