namespace RadioWash.Api.Services.Interfaces;

public interface IPaymentService
{
  /// <summary>
  /// Creates a Stripe Checkout session for the given (server-resolved) price. The optional
  /// clientRequestId feeds Stripe's idempotency key so request retries can't mint duplicate
  /// sessions.
  /// </summary>
  Task<string> CreateCheckoutSessionAsync(int userId, string planPriceId, string? clientRequestId = null);

  Task<string> CreatePortalSessionAsync(string customerId);

  /// <summary>
  /// Flags the Stripe subscription to cancel at the end of the current billing period. The
  /// user keeps access until then; customer.subscription.deleted performs local deactivation.
  /// </summary>
  Task CancelAtPeriodEndAsync(string stripeSubscriptionId);

  /// <summary>
  /// Retrieves a checkout session with its subscription expanded — used by the post-redirect
  /// reconcile endpoint so the frontend doesn't have to race the webhook.
  /// </summary>
  Task<Stripe.Checkout.Session> GetCheckoutSessionAsync(string sessionId);

  Task HandleWebhookAsync(string payload, string signature);
}
