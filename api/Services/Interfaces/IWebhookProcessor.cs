using Stripe;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Dispatches pre-verified Stripe webhook events to their handlers. Signature verification,
/// idempotency claiming, and retry scheduling live in the caller (StripePaymentService for
/// live deliveries, WebhookRetryService for replays) — this processor assumes the event is
/// authentic and throws on any processing failure so callers can record and retry it.
/// </summary>
public interface IWebhookProcessor
{
  Task ProcessEventAsync(Event stripeEvent);
}
