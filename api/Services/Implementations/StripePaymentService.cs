using RadioWash.Api.Services.Exceptions;
using RadioWash.Api.Services.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace RadioWash.Api.Services.Implementations;

public class StripePaymentService : IPaymentService
{
  private readonly IConfiguration _configuration;
  private readonly IEventUtility _eventUtility;
  private readonly IIdempotencyService _idempotencyService;
  private readonly IWebhookRetryService _webhookRetryService;
  private readonly IWebhookProcessor _webhookProcessor;
  private readonly SessionService _checkoutSessionService;
  private readonly Stripe.BillingPortal.SessionService _portalSessionService;
  private readonly Stripe.SubscriptionService _stripeSubscriptionService;
  private readonly ILogger<StripePaymentService> _logger;

  public StripePaymentService(
      IConfiguration configuration,
      IEventUtility eventUtility,
      IIdempotencyService idempotencyService,
      IWebhookRetryService webhookRetryService,
      IWebhookProcessor webhookProcessor,
      SessionService checkoutSessionService,
      Stripe.BillingPortal.SessionService portalSessionService,
      Stripe.SubscriptionService stripeSubscriptionService,
      ILogger<StripePaymentService> logger)
  {
    _configuration = configuration;
    _eventUtility = eventUtility;
    _idempotencyService = idempotencyService;
    _webhookRetryService = webhookRetryService;
    _webhookProcessor = webhookProcessor;
    _checkoutSessionService = checkoutSessionService;
    _portalSessionService = portalSessionService;
    _stripeSubscriptionService = stripeSubscriptionService;
    _logger = logger;

    StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
  }

  public async Task<string> CreateCheckoutSessionAsync(int userId, string planPriceId, string? clientRequestId = null)
  {
    var options = new SessionCreateOptions
    {
      PaymentMethodTypes = new List<string> { "card" },
      LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = planPriceId,
                    Quantity = 1
                }
            },
      Mode = "subscription",
      // {CHECKOUT_SESSION_ID} is substituted by Stripe; the success page uses it to
      // reconcile the subscription server-side instead of racing the webhook.
      SuccessUrl = $"{_configuration["FrontendUrl"]}/subscription/success?session_id={{CHECKOUT_SESSION_ID}}",
      CancelUrl = $"{_configuration["FrontendUrl"]}/subscription/cancel",
      Metadata = new Dictionary<string, string>
            {
                { "userId", userId.ToString() }
            },
      SubscriptionData = new SessionSubscriptionDataOptions
      {
        Metadata = new Dictionary<string, string>
        {
          { "userId", userId.ToString() }
        }
      }
    };

    var requestOptions = string.IsNullOrEmpty(clientRequestId)
      ? null
      : new RequestOptions { IdempotencyKey = $"checkout-{userId}-{clientRequestId}" };

    var session = await _checkoutSessionService.CreateAsync(options, requestOptions);

    if (string.IsNullOrEmpty(session.Url))
    {
      throw new InvalidOperationException($"Stripe checkout session {session.Id} has no redirect URL");
    }

    _logger.LogInformation("Created Stripe checkout session {SessionId} for user {UserId}", session.Id, userId);

    return session.Url;
  }

  public async Task<string> CreatePortalSessionAsync(string customerId)
  {
    var options = new Stripe.BillingPortal.SessionCreateOptions
    {
      Customer = customerId,
      ReturnUrl = $"{_configuration["FrontendUrl"]}/dashboard"
    };

    var session = await _portalSessionService.CreateAsync(options);

    return session.Url;
  }

  public async Task CancelAtPeriodEndAsync(string stripeSubscriptionId)
  {
    await _stripeSubscriptionService.UpdateAsync(stripeSubscriptionId, new SubscriptionUpdateOptions
    {
      CancelAtPeriodEnd = true
    });

    _logger.LogInformation("Requested cancel-at-period-end for Stripe subscription {SubscriptionId}", stripeSubscriptionId);
  }

  public async Task<Session> GetCheckoutSessionAsync(string sessionId)
  {
    return await _checkoutSessionService.GetAsync(sessionId, new SessionGetOptions
    {
      Expand = new List<string> { "subscription" }
    });
  }

  public async Task HandleWebhookAsync(string payload, string signature)
  {
    var webhookSecret = _configuration["Stripe:WebhookSecret"];

    if (string.IsNullOrEmpty(webhookSecret))
    {
      _logger.LogError("Stripe webhook secret is not configured");
      throw new InvalidOperationException("Stripe webhook secret is not configured");
    }

    Event stripeEvent;
    try
    {
      stripeEvent = _eventUtility.ConstructEvent(payload, signature, webhookSecret);
    }
    catch (StripeException ex)
    {
      // Only verification/parsing of the incoming payload maps to a permanent rejection
      // (HTTP 400). StripeExceptions thrown during processing must NOT land here — they
      // propagate below so Stripe redelivers.
      _logger.LogError(ex, "Stripe webhook signature verification failed: {Message}", ex.Message);
      throw new WebhookSignatureVerificationException("Stripe webhook signature verification failed", ex);
    }

    _logger.LogInformation("Processing Stripe webhook event: {EventType} with ID {EventId}",
        stripeEvent.Type, stripeEvent.Id);

    // Use idempotency service to ensure only one concurrent request processes this event
    var shouldProcess = await _idempotencyService.TryProcessEventAsync(stripeEvent.Id, stripeEvent.Type);

    if (!shouldProcess)
    {
      _logger.LogInformation("Webhook event {EventId} of type {EventType} has already been processed or claimed by another request",
          stripeEvent.Id, stripeEvent.Type);
      return;
    }

    try
    {
      await _webhookProcessor.ProcessEventAsync(stripeEvent);

      await _idempotencyService.MarkEventSuccessfulAsync(stripeEvent.Id);

      _logger.LogInformation("Successfully processed webhook event {EventId} of type {EventType}",
          stripeEvent.Id, stripeEvent.Type);
    }
    catch (Exception processingEx)
    {
      // Log the original failure first — if releasing the claim below also fails (DB down),
      // that secondary error must not mask this one.
      _logger.LogError(processingEx, "Failed to process webhook event {EventId} of type {EventType}: {ErrorMessage}",
          stripeEvent.Id, stripeEvent.Type, processingEx.Message);

      try
      {
        // Marking the event failed releases the idempotency claim so Stripe's redelivery
        // (triggered by the controller's 500) can re-attempt it.
        await _idempotencyService.MarkEventFailedAsync(stripeEvent.Id, processingEx.Message);
      }
      catch (Exception markEx)
      {
        // Claim stays Processing; the stale-claim takeover unblocks it after 15 minutes.
        _logger.LogError(markEx, "Failed to release idempotency claim for webhook event {EventId}", stripeEvent.Id);
      }

      // Schedule retry if the error is retryable
      if (_webhookRetryService.IsRetryableError(processingEx))
      {
        try
        {
          await _webhookRetryService.ScheduleRetryAsync(
            stripeEvent.Id,
            stripeEvent.Type,
            payload,
            signature,
            processingEx.Message);

          _logger.LogInformation("Scheduled retry for webhook event {EventId} due to retryable error", stripeEvent.Id);
        }
        catch (Exception retryEx)
        {
          _logger.LogError(retryEx, "Failed to schedule retry for webhook event {EventId}: {RetryError}",
            stripeEvent.Id, retryEx.Message);
        }
      }
      else
      {
        _logger.LogWarning("Webhook event {EventId} failed with non-retryable error: {ErrorMessage}",
          stripeEvent.Id, processingEx.Message);
      }

      throw;
    }
  }
}
