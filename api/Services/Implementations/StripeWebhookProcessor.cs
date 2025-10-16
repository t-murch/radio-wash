using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace RadioWash.Api.Services.Implementations;

public class StripeWebhookProcessor : IWebhookProcessor
{
    private readonly IConfiguration _configuration;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IEventUtility _eventUtility;
    private readonly RadioWashDbContext _dbContext;
    private readonly CustomerService _customerService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<StripeWebhookProcessor> _logger;

    public StripeWebhookProcessor(
        IConfiguration configuration,
        ISubscriptionService subscriptionService,
        IEventUtility eventUtility,
        RadioWashDbContext dbContext,
        CustomerService customerService,
        IIdempotencyService idempotencyService,
        ILogger<StripeWebhookProcessor> logger)
    {
        _configuration = configuration;
        _subscriptionService = subscriptionService;
        _eventUtility = eventUtility;
        _dbContext = dbContext;
        _customerService = customerService;
        _idempotencyService = idempotencyService;
        _logger = logger;

        StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
    }

    public async Task ProcessWebhookAsync(string payload, string signature)
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];

        if (string.IsNullOrEmpty(webhookSecret))
        {
            _logger.LogError("Stripe webhook secret is not configured");
            throw new InvalidOperationException("Stripe webhook secret is not configured");
        }

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

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(stripeEvent);
                break;
            case "customer.subscription.created":
                await HandleSubscriptionCreatedAsync(stripeEvent);
                break;
            case "customer.subscription.updated":
                await HandleSubscriptionUpdatedAsync(stripeEvent);
                break;
            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync(stripeEvent);
                break;
            case "invoice.payment_failed":
                await HandlePaymentFailedAsync(stripeEvent);
                break;
            case "invoice.payment_succeeded":
                await HandlePaymentSucceededAsync(stripeEvent);
                break;
            default:
                _logger.LogInformation("Unhandled webhook event type: {EventType}", stripeEvent.Type);
                break;
        }

        // Mark event as successfully processed
        await _idempotencyService.MarkEventSuccessfulAsync(stripeEvent.Id);

        _logger.LogInformation("Successfully processed webhook event {EventId} of type {EventType}",
            stripeEvent.Id, stripeEvent.Type);
    }

    // All the existing handler methods from StripePaymentService
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

            if (subscription.Items?.Data?.Any() != true)
            {
                _logger.LogWarning("Subscription {SubscriptionId} has no items", subscription.Id);
                return;
            }

            var priceId = subscription.Items.Data.First().Price.Id;
            _logger.LogInformation("Found price ID {PriceId} for subscription {SubscriptionId}", priceId, subscription.Id);

            var plan = await _subscriptionService.GetPlanByStripePriceIdAsync(priceId);
            if (plan == null)
            {
                _logger.LogError("No local plan found for Stripe price ID {PriceId}", priceId);
                return;
            }

            int? userId = null;

            try
            {
                if (subscription.Metadata?.TryGetValue("userId", out var userIdStr) == true &&
                    int.TryParse(userIdStr, out var parsedUserId))
                {
                    userId = parsedUserId;
                    _logger.LogInformation("Found user ID {UserId} in subscription metadata for subscription {SubscriptionId}",
                        userId, subscription.Id);
                }
                else
                {
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

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                await _subscriptionService.CreateSubscriptionAsync(
                    userId.Value,
                    plan.Id,
                    subscription.Id,
                    subscription.CustomerId);

                await transaction.CommitAsync();

                _logger.LogInformation("Successfully created subscription record for user {UserId}, subscription {SubscriptionId}",
                    userId, subscription.Id);
            }
            catch (Exception dbEx)
            {
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
}