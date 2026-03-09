using Stripe;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Processes webhook events without retry logic to avoid circular dependencies
/// </summary>
public interface IWebhookProcessor
{
  /// <summary>
  /// Processes a verified webhook event
  /// </summary>
  Task ProcessWebhookAsync(Event stripeEvent);
}