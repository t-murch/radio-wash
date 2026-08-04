using Stripe;

namespace RadioWash.Api.Services.Interfaces;

public interface IEventUtility
{
  Event ConstructEvent(string payload, string signature, string secret);

  /// <summary>
  /// Deserializes a webhook payload WITHOUT signature verification. Only for payloads that
  /// were already verified when first received (the internal retry queue): Stripe's
  /// signature timestamp tolerance (5 minutes) makes re-verification of a stored payload
  /// impossible by design.
  /// </summary>
  Event ParseEvent(string payload);
}
