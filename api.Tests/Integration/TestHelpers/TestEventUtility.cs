using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Tests.Integration.TestHelpers;

/// <summary>
/// Test implementation of IEventUtility that bypasses Stripe signature verification.
/// Parses the webhook payload JSON directly without validating the HMAC signature,
/// since we can't generate valid signatures without the Stripe CLI proxy.
/// </summary>
public class TestEventUtility : IEventUtility
{
    public Event ConstructEvent(string payload, string signature, string secret)
    {
        // Parse the event without signature verification
        return EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);
    }
}
