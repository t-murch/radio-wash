using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace RadioWash.Api.Services.Implementations;

public class StripeWebhookProcessor : IWebhookProcessor
{
    private readonly IConfiguration _configuration;
    private readonly ISubscriptionService _subscriptionService;
    private readonly CustomerService _customerService;
    private readonly Stripe.SubscriptionService _stripeSubscriptionService;
    private readonly ILogger<StripeWebhookProcessor> _logger;

    public StripeWebhookProcessor(
        IConfiguration configuration,
        ISubscriptionService subscriptionService,
        CustomerService customerService,
        Stripe.SubscriptionService stripeSubscriptionService,
        ILogger<StripeWebhookProcessor> logger)
    {
        _configuration = configuration;
        _subscriptionService = subscriptionService;
        _customerService = customerService;
        _stripeSubscriptionService = stripeSubscriptionService;
        _logger = logger;

        StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
    }

    public async Task ProcessEventAsync(Event stripeEvent)
    {
        _logger.LogInformation("Processing Stripe webhook event: {EventType} with ID {EventId}",
            stripeEvent.Type, stripeEvent.Id);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(stripeEvent);
                break;
            case "customer.subscription.created":
            case "customer.subscription.updated":
                await HandleSubscriptionChangedAsync(stripeEvent);
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

        _logger.LogInformation("Successfully processed webhook event {EventId} of type {EventType}",
            stripeEvent.Id, stripeEvent.Type);
    }

    private Task HandleCheckoutCompletedAsync(Event stripeEvent)
    {
        // The subscription itself is handled by customer.subscription.created/updated;
        // checkout completion is informational only.
        var session = stripeEvent.Data.Object as Session;
        if (session == null)
        {
            _logger.LogWarning("Checkout completed event {EventId} has no session object", stripeEvent.Id);
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

    // Handles both customer.subscription.created and .updated: SyncFromStripeAsync upserts
    // keyed on the Stripe subscription id, so ordering between the two doesn't matter.
    // The event's embedded subscription is only used as a pointer — the state written comes
    // from a fresh fetch (see SyncSubscriptionFromStripeAsync), because a delayed redelivery
    // of an old `updated` event (status active) after cancellation would otherwise
    // resurrect the canceled subscription.
    private async Task HandleSubscriptionChangedAsync(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription
            ?? throw new InvalidOperationException(
                $"Event {stripeEvent.Id} ({stripeEvent.Type}) has no subscription object");

        await SyncSubscriptionFromStripeAsync(subscription.Id);
    }

    private async Task HandleSubscriptionDeletedAsync(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription
            ?? throw new InvalidOperationException(
                $"Event {stripeEvent.Id} (customer.subscription.deleted) has no subscription object");

        var local = await _subscriptionService.GetByStripeSubscriptionIdAsync(subscription.Id);
        if (local == null)
        {
            // Nothing to cancel locally; the reconciliation job owns any remaining drift.
            _logger.LogWarning("Subscription {SubscriptionId} deleted on Stripe but no local record exists", subscription.Id);
            return;
        }

        await _subscriptionService.UpdateSubscriptionStatusAsync(subscription.Id, SubscriptionStatus.Canceled);

        _logger.LogInformation("Subscription {SubscriptionId} deleted", subscription.Id);
    }

    private async Task HandlePaymentFailedAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice
            ?? throw new InvalidOperationException(
                $"Event {stripeEvent.Id} (invoice.payment_failed) has no invoice object");

        var subscriptionId = GetSubscriptionIdFromInvoice(invoice);
        if (string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogInformation("Payment failed for invoice {InvoiceId} (not subscription-related)", invoice.Id);
            return;
        }

        _logger.LogWarning("Payment failed for subscription {SubscriptionId} from invoice {InvoiceId}",
            subscriptionId, invoice.Id);

        await SyncSubscriptionFromStripeAsync(subscriptionId);
    }

    private async Task HandlePaymentSucceededAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice
            ?? throw new InvalidOperationException(
                $"Event {stripeEvent.Id} (invoice.payment_succeeded) has no invoice object");

        var subscriptionId = GetSubscriptionIdFromInvoice(invoice);
        if (string.IsNullOrEmpty(subscriptionId))
        {
            _logger.LogInformation("Payment succeeded for invoice {InvoiceId} (not subscription-related)", invoice.Id);
            return;
        }

        _logger.LogInformation("Payment succeeded for subscription {SubscriptionId}, invoice {InvoiceId}",
            subscriptionId, invoice.Id);

        await SyncSubscriptionFromStripeAsync(subscriptionId);
    }

    // Events are treated as pointers, never as state: always fetch the subscription's
    // CURRENT state from Stripe and upsert that. Applying an event's embedded snapshot (or
    // deriving a status from the event type, e.g. payment_succeeded => active) breaks on
    // delayed redeliveries: Stripe can redeliver an old event for up to 3 days, and a stale
    // "active" then would resurrect a subscription that was since canceled. Canceled
    // subscriptions stay retrievable via GetAsync, so the fetch works for every lifecycle
    // stage this processor handles.
    private async Task SyncSubscriptionFromStripeAsync(string subscriptionId)
    {
        var stripeSubscription = await _stripeSubscriptionService.GetAsync(subscriptionId);
        await _subscriptionService.SyncFromStripeAsync(
            stripeSubscription,
            () => ResolveUserIdFromCustomerAsync(stripeSubscription.CustomerId, subscriptionId));
    }

    // Fallback userId resolution for subscriptions without userId metadata (e.g. created
    // outside the app's checkout flow): look at the Stripe customer's metadata.
    private async Task<int?> ResolveUserIdFromCustomerAsync(string customerId, string subscriptionId)
    {
        try
        {
            var customer = await _customerService.GetAsync(customerId);
            if (customer?.Metadata?.TryGetValue("userId", out var userIdStr) == true
                && int.TryParse(userIdStr, out var userId))
            {
                _logger.LogInformation(
                    "Found user ID {UserId} in customer metadata for subscription {SubscriptionId}",
                    userId, subscriptionId);
                return userId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to look up customer {CustomerId} for subscription {SubscriptionId}",
                customerId, subscriptionId);
        }

        return null;
    }

    private string? GetSubscriptionIdFromInvoice(Invoice invoice)
    {
        // v49 compatibility: the subscription reference is only reliably present in the raw
        // webhook JSON, either as a string id or an expanded object.
        // A missing key and an explicit JSON null both mean "not subscription-related".
        var subscriptionValue = invoice.RawJObject?["subscription"];
        if (subscriptionValue == null || subscriptionValue.Type == Newtonsoft.Json.Linq.JTokenType.Null)
        {
            return null;
        }

        return subscriptionValue.Type == Newtonsoft.Json.Linq.JTokenType.String
            ? subscriptionValue.ToString()
            : subscriptionValue["id"]?.ToString();
    }
}
