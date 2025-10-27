using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace RadioWash.Api.Services.Implementations;

public class StripePaymentService : IPaymentService
{
  private readonly IConfiguration _configuration;
  private readonly ISubscriptionService _subscriptionService;
  private readonly IEventUtility _eventUtility;
  private readonly RadioWashDbContext _dbContext;
  private readonly CustomerService _customerService;
  private readonly IIdempotencyService _idempotencyService;
  private readonly IWebhookRetryService _webhookRetryService;
  private readonly IWebhookProcessor _webhookProcessor;
  private readonly ILogger<StripePaymentService> _logger;

  public StripePaymentService(
      IConfiguration configuration,
      ISubscriptionService subscriptionService,
      IEventUtility eventUtility,
      RadioWashDbContext dbContext,
      CustomerService customerService,
      IIdempotencyService idempotencyService,
      IWebhookRetryService webhookRetryService,
      IWebhookProcessor webhookProcessor,
      ILogger<StripePaymentService> logger)
  {
    _configuration = configuration;
    _subscriptionService = subscriptionService;
    _eventUtility = eventUtility;
    _dbContext = dbContext;
    _customerService = customerService;
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

    try
    {
      var stripeEvent = _eventUtility.ConstructEvent(payload, signature, webhookSecret);

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
        // Use the webhook processor for actual event processing
        await _webhookProcessor.ProcessWebhookAsync(payload, signature);

        // Mark event as successfully processed
        await _idempotencyService.MarkEventSuccessfulAsync(stripeEvent.Id);

        _logger.LogInformation("Successfully processed webhook event {EventId} of type {EventType}", 
            stripeEvent.Id, stripeEvent.Type);
      }
      catch (Exception processingEx)
      {
        // Mark event as failed
        await _idempotencyService.MarkEventFailedAsync(stripeEvent.Id, processingEx.Message);

        _logger.LogError(processingEx, "Failed to process webhook event {EventId} of type {EventType}: {ErrorMessage}", 
            stripeEvent.Id, stripeEvent.Type, processingEx.Message);

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
    catch (StripeException ex)
    {
      _logger.LogError(ex, "Stripe webhook signature verification failed: {Message}", ex.Message);
      throw;
    }
  }

}
