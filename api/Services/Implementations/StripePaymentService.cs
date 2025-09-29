using RadioWash.Api.Services.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace RadioWash.Api.Services.Implementations;

public class StripePaymentService : IPaymentService
{
    private readonly IConfiguration _configuration;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<StripePaymentService> _logger;

    public StripePaymentService(
        IConfiguration configuration,
        ISubscriptionService subscriptionService,
        ILogger<StripePaymentService> logger)
    {
        _configuration = configuration;
        _subscriptionService = subscriptionService;
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
        
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);
            
            _logger.LogInformation("Processing Stripe webhook event: {EventType}", stripeEvent.Type);

            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutCompletedAsync(stripeEvent);
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
                default:
                    _logger.LogInformation("Unhandled webhook event type: {EventType}", stripeEvent.Type);
                    break;
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe webhook error: {Message}", ex.Message);
            throw;
        }
    }

    private async Task HandleCheckoutCompletedAsync(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session == null) return;

        if (session.Metadata.TryGetValue("userId", out var userIdStr) && int.TryParse(userIdStr, out var userId))
        {
            _logger.LogInformation("Checkout completed for user {UserId}, session {SessionId}", userId, session.Id);
            
            // The subscription will be handled by the subscription.created event
            // For now, we just log the successful checkout
        }
    }

    private async Task HandleSubscriptionUpdatedAsync(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null) return;

        await _subscriptionService.UpdateSubscriptionStatusAsync(subscription.Id, subscription.Status);
        
        await _subscriptionService.UpdateSubscriptionDatesAsync(
            subscription.Id,
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd
        );

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

        if (!string.IsNullOrEmpty(invoice.SubscriptionId))
        {
            await _subscriptionService.UpdateSubscriptionStatusAsync(invoice.SubscriptionId, "past_due");
            _logger.LogWarning("Payment failed for subscription {SubscriptionId}", invoice.SubscriptionId);
        }
    }
}