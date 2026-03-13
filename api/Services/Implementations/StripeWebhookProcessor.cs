using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace RadioWash.Api.Services.Implementations;

public class StripeWebhookProcessor : IWebhookProcessor
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly CustomerService _customerService;
    private readonly ILogger<StripeWebhookProcessor> _logger;

    public StripeWebhookProcessor(
        ISubscriptionService subscriptionService,
        CustomerService customerService,
        ILogger<StripeWebhookProcessor> logger)
    {
        _subscriptionService = subscriptionService;
        _customerService = customerService;
        _logger = logger;
    }

    public async Task ProcessWebhookAsync(Event stripeEvent)
    {
        _logger.LogInformation("Processing Stripe webhook event: {EventType} with ID {EventId}",
            stripeEvent.Type, stripeEvent.Id);

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

    }

    private Task HandleCheckoutCompletedAsync(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session == null)
        {
            _logger.LogWarning("Checkout completed event received but session object is null");
            return Task.CompletedTask;
        }

        if (session.Metadata?.TryGetValue("userId", out var userIdStr) == true && int.TryParse(userIdStr, out var userId))
        {
            _logger.LogInformation("Checkout completed for user {UserId}, session {SessionId}", userId, session.Id);
        }
        else
        {
            _logger.LogWarning("Checkout completed for session {SessionId} but no valid userId found in metadata", session.Id);
        }

        return Task.CompletedTask;
    }

    private async Task HandleSubscriptionUpdatedAsync(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null)
        {
            _logger.LogWarning("Subscription updated event received but subscription object is null");
            return;
        }

        var status = subscription.Status;
        if (subscription.CancelAtPeriodEnd && status == "active")
        {
            status = SubscriptionStatus.CancelAtPeriodEnd;
        }
        await _subscriptionService.UpdateSubscriptionStatusAsync(subscription.Id, status);

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
            subscription.Id, status);
    }

    private async Task HandleSubscriptionDeletedAsync(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null)
        {
            _logger.LogWarning("Subscription deleted event received but subscription object is null");
            return;
        }

        await _subscriptionService.UpdateSubscriptionStatusAsync(subscription.Id, "canceled");

        _logger.LogInformation("Subscription {SubscriptionId} deleted", subscription.Id);
    }

    private async Task HandlePaymentFailedAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null) return;

        // Handle v49 compatibility - get subscription ID from RawJObject if direct property not available
        string? subscriptionId = ExtractSubscriptionIdFromInvoice(invoice, invoice.Id);

        if (!string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogInformation("Retrieved subscription ID {SubscriptionId} from invoice {InvoiceId} webhook",
                subscriptionId, invoice.Id);
        }
        else
        {
            _logger.LogWarning("No subscription reference found in invoice {InvoiceId} webhook payload", invoice.Id);
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
                throw;
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
                throw new InvalidOperationException(
                    $"Subscription {subscription.Id} has no items — cannot create subscription record");
            }

            var priceId = subscription.Items.Data.First().Price.Id;
            _logger.LogInformation("Found price ID {PriceId} for subscription {SubscriptionId}", priceId, subscription.Id);

            // Find the local plan by Stripe price ID
            var plan = await _subscriptionService.GetPlanByStripePriceIdAsync(priceId);
            if (plan == null)
            {
                throw new InvalidOperationException(
                    $"No local plan found for Stripe price ID {priceId} — cannot create subscription record");
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
                throw new InvalidOperationException(
                    $"Could not determine user ID for subscription {subscription.Id} — cannot create subscription record");
            }

            await _subscriptionService.CreateSubscriptionAsync(
                userId.Value,
                plan.Id,
                subscription.Id,
                subscription.CustomerId);

            _logger.LogInformation("Successfully created subscription record for user {UserId}, subscription {SubscriptionId}",
                userId, subscription.Id);
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
            string? subscriptionId = ExtractSubscriptionIdFromInvoice(invoice, invoice.Id);

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
            throw;
        }
    }

    private string? ExtractSubscriptionIdFromInvoice(Invoice invoice, string invoiceId)
    {
        try
        {
            var subscriptionValue = invoice.RawJObject?["subscription"];
            if (subscriptionValue != null)
            {
                return subscriptionValue.Type == Newtonsoft.Json.Linq.JTokenType.String
                    ? subscriptionValue.ToString()
                    : subscriptionValue["id"]?.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract subscription ID from invoice {InvoiceId}", invoiceId);
        }

        return null;
    }
}