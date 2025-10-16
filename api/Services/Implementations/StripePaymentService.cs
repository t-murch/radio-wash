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
    try
    {
      // Delegate to the webhook processor for the actual processing
      await _webhookProcessor.ProcessWebhookAsync(payload, signature);
    }
    catch (Exception processingEx)
    {
      // Schedule retry if the error is retryable
      if (_webhookRetryService.IsRetryableError(processingEx))
      {
        // Extract event ID for retry tracking
        var eventId = await ExtractEventIdAsync(payload, signature);
        var eventType = await ExtractEventTypeAsync(payload, signature);

        if (!string.IsNullOrEmpty(eventId) && !string.IsNullOrEmpty(eventType))
        {
          await _webhookRetryService.ScheduleRetryAsync(
              eventId, 
              eventType, 
              payload, 
              signature, 
              processingEx.Message, 
              1); // First retry attempt

          _logger.LogWarning(processingEx, "Webhook event {EventId} processing failed, retry scheduled: {ErrorMessage}", 
              eventId, processingEx.Message);
        }
        else
        {
          _logger.LogError(processingEx, "Failed to extract event details for retry scheduling: {ErrorMessage}", 
              processingEx.Message);
        }
      }
      else
      {
        _logger.LogError(processingEx, "Webhook processing failed with non-retryable error: {ErrorMessage}", 
            processingEx.Message);
      }

      throw;
    }
  }

  private async Task<string?> ExtractEventIdAsync(string payload, string signature)
  {
    try
    {
      var webhookSecret = _configuration["Stripe:WebhookSecret"];
      if (string.IsNullOrEmpty(webhookSecret)) return null;

      var stripeEvent = _eventUtility.ConstructEvent(payload, signature, webhookSecret);
      return stripeEvent.Id;
    }
    catch
    {
      return null;
    }
  }

  private async Task<string?> ExtractEventTypeAsync(string payload, string signature)
  {
    try
    {
      var webhookSecret = _configuration["Stripe:WebhookSecret"];
      if (string.IsNullOrEmpty(webhookSecret)) return null;

      var stripeEvent = _eventUtility.ConstructEvent(payload, signature, webhookSecret);
      return stripeEvent.Type;
    }
    catch
    {
      return null;
    }
  }

  private async Task HandleCheckoutCompletedAsync(Event stripeEvent)
  {
    var session = stripeEvent.Data.Object as Session;
    if (session == null)
    {
      _logger.LogWarning("Checkout completed event received but session object is null");
      return;
    }

    try
    {
      if (session.Metadata?.TryGetValue("userId", out var userIdStr) == true && int.TryParse(userIdStr, out var userId))
      {
        _logger.LogInformation("Checkout completed for user {UserId}, session {SessionId}", userId, session.Id);

        // The subscription will be handled by the subscription.created event
        // For now, we just log the successful checkout
      }
      else
      {
        _logger.LogWarning("Checkout completed for session {SessionId} but no valid userId found in metadata", session.Id);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing checkout completed event for session {SessionId}", session.Id);
    }

    // Add await to satisfy async method warning
    await Task.CompletedTask;
  }

  private async Task HandleSubscriptionUpdatedAsync(Event stripeEvent)
  {
    var subscription = stripeEvent.Data.Object as Stripe.Subscription;
    if (subscription == null) return;

    await _subscriptionService.UpdateSubscriptionStatusAsync(subscription.Id, subscription.Status);

    // Get period dates from subscription items (v49 compatibility)
    DateTime? currentPeriodStart = null;
    DateTime? currentPeriodEnd = null;

    try
    {
      if (subscription.Items?.Data?.Any() == true)
      {
        // For single-item subscriptions, use the first item's period dates
        // For multi-item subscriptions, use the latest period end
        var subscriptionItem = subscription.Items.Data.First();
        currentPeriodStart = subscriptionItem.CurrentPeriodStart;
        currentPeriodEnd = subscription.Items.Data.Max(x => x.CurrentPeriodEnd);

        _logger.LogInformation("Retrieved period dates from subscription items for {SubscriptionId}: Start={Start}, End={End}",
            subscription.Id, currentPeriodStart, currentPeriodEnd);
      }
      else
      {
        _logger.LogWarning("No subscription items found for subscription {SubscriptionId}, cannot update period dates",
            subscription.Id);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error retrieving period dates from subscription items for {SubscriptionId}",
          subscription.Id);
    }

    // Only update dates if we successfully retrieved them
    if (currentPeriodStart.HasValue && currentPeriodEnd.HasValue)
    {
      await _subscriptionService.UpdateSubscriptionDatesAsync(
          subscription.Id,
          currentPeriodStart.Value,
          currentPeriodEnd.Value
      );
    }

    _logger.LogInformation("Updated subscription {SubscriptionId} status to {Status}",
        subscription.Id, subscription.Status);
  }

  private async Task HandleSubscriptionDeletedAsync(Event stripeEvent)
  {
    var subscription = stripeEvent.Data.Object as Stripe.Subscription;
    if (subscription == null) return;

    await _subscriptionService.UpdateSubscriptionStatusAsync(subscription.Id, "canceled");

    _logger.LogInformation("Subscription {SubscriptionId} deleted", subscription.Id);
  }

  private async Task HandlePaymentFailedAsync(Event stripeEvent)
  {
    var invoice = stripeEvent.Data.Object as Invoice;
    if (invoice == null) return;

    // Handle v49 compatibility - get subscription ID from RawJObject if direct property not available
    string? subscriptionId = null;

    try
    {
      // Try to get subscription ID from RawJObject (always available in webhook events)
      var subscriptionValue = invoice.RawJObject?["subscription"];
      if (subscriptionValue != null)
      {
        subscriptionId = subscriptionValue.Type == Newtonsoft.Json.Linq.JTokenType.String
          ? subscriptionValue.ToString()
          : subscriptionValue["id"]?.ToString();
      }

      if (!string.IsNullOrEmpty(subscriptionId))
      {
        _logger.LogInformation("Retrieved subscription ID {SubscriptionId} from invoice {InvoiceId} webhook",
            subscriptionId, invoice.Id);
      }
      else
      {
        _logger.LogWarning("No subscription reference found in invoice {InvoiceId} webhook payload", invoice.Id);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error accessing subscription information from invoice {InvoiceId} webhook", invoice.Id);
    }

    if (!string.IsNullOrEmpty(subscriptionId))
    {
      try
      {
        await _subscriptionService.UpdateSubscriptionStatusAsync(subscriptionId, "past_due");
        _logger.LogWarning("Payment failed for subscription {SubscriptionId} from invoice {InvoiceId}",
            subscriptionId, invoice.Id);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to update subscription {SubscriptionId} status after payment failure for invoice {InvoiceId}",
            subscriptionId, invoice.Id);
      }
    }
    else
    {
      _logger.LogWarning("Could not determine subscription ID for failed payment on invoice {InvoiceId}", invoice.Id);
    }
  }

  private async Task HandleSubscriptionCreatedAsync(Event stripeEvent)
  {
    var subscription = stripeEvent.Data.Object as Stripe.Subscription;
    if (subscription == null)
    {
      _logger.LogWarning("Subscription created event received but subscription object is null");
      return;
    }

    try
    {
      _logger.LogInformation("Processing subscription creation for {SubscriptionId}", subscription.Id);

      // Get the price ID from the subscription items
      if (subscription.Items?.Data?.Any() != true)
      {
        _logger.LogWarning("Subscription {SubscriptionId} has no items", subscription.Id);
        return;
      }

      var priceId = subscription.Items.Data.First().Price.Id;
      _logger.LogInformation("Found price ID {PriceId} for subscription {SubscriptionId}", priceId, subscription.Id);

      // Find the local plan by Stripe price ID
      var plan = await _subscriptionService.GetPlanByStripePriceIdAsync(priceId);
      if (plan == null)
      {
        _logger.LogError("No local plan found for Stripe price ID {PriceId}", priceId);
        return;
      }

      // Get user ID from subscription metadata
      int? userId = null;
      
      try
      {
        // Try to get user ID from subscription metadata first
        if (subscription.Metadata?.TryGetValue("userId", out var userIdStr) == true && 
            int.TryParse(userIdStr, out var parsedUserId))
        {
          userId = parsedUserId;
          _logger.LogInformation("Found user ID {UserId} in subscription metadata for subscription {SubscriptionId}", 
              userId, subscription.Id);
        }
        else
        {
          // Fallback: try customer metadata (for existing subscriptions)
          var customer = await _customerService.GetAsync(subscription.CustomerId);
          
          if (customer?.Metadata?.TryGetValue("userId", out userIdStr) == true && 
              int.TryParse(userIdStr, out parsedUserId))
          {
            userId = parsedUserId;
            _logger.LogInformation("Found user ID {UserId} in customer metadata for subscription {SubscriptionId}", 
                userId, subscription.Id);
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to retrieve user ID for subscription {SubscriptionId}", subscription.Id);
      }

      if (!userId.HasValue)
      {
        _logger.LogError("Could not determine user ID for subscription {SubscriptionId}", subscription.Id);
        return;
      }

      // Use transaction only for the database write operation to ensure atomicity
      using var transaction = await _dbContext.Database.BeginTransactionAsync();
      
      try
      {
        // Create the subscription record within the transaction
        await _subscriptionService.CreateSubscriptionAsync(
            userId.Value, 
            plan.Id, 
            subscription.Id, 
            subscription.CustomerId);

        // Commit the transaction if subscription creation succeeded
        await transaction.CommitAsync();
        
        _logger.LogInformation("Successfully created subscription record for user {UserId}, subscription {SubscriptionId}", 
            userId, subscription.Id);
      }
      catch (Exception dbEx)
      {
        // Rollback the transaction on database operation failure
        await transaction.RollbackAsync();
        _logger.LogError(dbEx, "Failed to create subscription record for user {UserId}, subscription {SubscriptionId}. Transaction rolled back.", 
            userId, subscription.Id);
        throw;
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing subscription created event for subscription {SubscriptionId}", subscription.Id);
      throw;
    }
  }

  private async Task HandlePaymentSucceededAsync(Event stripeEvent)
  {
    var invoice = stripeEvent.Data.Object as Invoice;
    if (invoice == null)
    {
      _logger.LogWarning("Payment succeeded event received but invoice object is null");
      return;
    }

    try
    {
      // Get subscription ID from invoice
      string? subscriptionId = null;

      try
      {
        var subscriptionValue = invoice.RawJObject?["subscription"];
        if (subscriptionValue != null)
        {
          subscriptionId = subscriptionValue.Type == Newtonsoft.Json.Linq.JTokenType.String
            ? subscriptionValue.ToString()
            : subscriptionValue["id"]?.ToString();
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to extract subscription ID from invoice {InvoiceId}", invoice.Id);
      }

      if (!string.IsNullOrEmpty(subscriptionId))
      {
        _logger.LogInformation("Payment succeeded for subscription {SubscriptionId}, invoice {InvoiceId}", 
            subscriptionId, invoice.Id);
        
        // Update subscription status to active (in case it was incomplete)
        await _subscriptionService.UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Active);
      }
      else
      {
        _logger.LogInformation("Payment succeeded for invoice {InvoiceId} (not subscription-related)", invoice.Id);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing payment succeeded event for invoice {InvoiceId}", invoice.Id);
    }

    await Task.CompletedTask;
  }

  private static bool IsUniqueConstraintViolation(DbUpdateException ex)
  {
    // Check for SQL Server unique constraint violation
    if (ex.InnerException?.Message?.Contains("duplicate key") == true ||
        ex.InnerException?.Message?.Contains("UNIQUE constraint") == true ||
        ex.InnerException?.Message?.Contains("unique constraint") == true)
    {
      return true;
    }

    // Check for SQLite unique constraint violation
    if (ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true)
    {
      return true;
    }

    // Check for PostgreSQL unique constraint violation
    if (ex.InnerException?.Message?.Contains("duplicate key value violates unique constraint") == true)
    {
      return true;
    }

    return false;
  }
}
