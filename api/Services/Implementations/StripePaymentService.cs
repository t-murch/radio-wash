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
  private readonly ILogger<StripePaymentService> _logger;

  public StripePaymentService(
      IConfiguration configuration,
      IEventUtility eventUtility,
      IIdempotencyService idempotencyService,
      IWebhookRetryService webhookRetryService,
      IWebhookProcessor webhookProcessor,
      ILogger<StripePaymentService> logger)
  {
    _configuration = configuration;
    _eventUtility = eventUtility;
    _idempotencyService = idempotencyService;
    _webhookRetryService = webhookRetryService;
    _webhookProcessor = webhookProcessor;
    _logger = logger;

    StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
  }

  public async Task<string> CreateCheckoutSessionAsync(int userId, string planPriceId)
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
      SuccessUrl = $"{_configuration["FrontendUrl"]}/subscription/success",
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

    var service = new SessionService();
    var session = await service.CreateAsync(options);

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

    var service = new Stripe.BillingPortal.SessionService();
    var session = await service.CreateAsync(options);

    return session.Url;
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
