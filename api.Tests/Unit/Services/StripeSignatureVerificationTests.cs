using System.Security.Cryptography;
using System.Text;
using RadioWash.Api.Services.Implementations;
using Stripe;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Exercises the REAL Stripe signature verification through EventUtilityWrapper (no mocks).
/// Documents the security properties the webhook pipeline relies on: authentic payloads
/// verify, tampered payloads are rejected, and stale timestamps are rejected — which is
/// exactly why the internal retry path must use ParseEvent instead of re-verifying.
/// </summary>
public class StripeSignatureVerificationTests
{
  private const string WebhookSecret = "whsec_test_secret_for_signature_tests";

  private readonly EventUtilityWrapper _eventUtility = new();

  /// <summary>
  /// Builds a minimal event payload whose api_version matches the SDK's expected version,
  /// so ConstructEvent's default API-version check passes.
  /// </summary>
  private static string BuildEventPayload(string eventId = "evt_sig_test", long? created = null)
  {
    var createdUnix = created ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var apiVersion = StripeConfiguration.ApiVersion;

    return "{"
        + $"\"id\":\"{eventId}\","
        + "\"object\":\"event\","
        + $"\"api_version\":\"{apiVersion}\","
        + $"\"created\":{createdUnix},"
        + "\"data\":{\"object\":{\"id\":\"cus_sig_test\",\"object\":\"customer\"}},"
        + "\"livemode\":false,"
        + "\"pending_webhooks\":1,"
        + "\"request\":{\"id\":\"req_sig_test\",\"idempotency_key\":null},"
        + "\"type\":\"customer.created\""
        + "}";
  }

  /// <summary>
  /// Computes a real Stripe-Signature header: t={unixSeconds},v1={HMACSHA256hex(secret, "{t}.{payload}")}.
  /// </summary>
  private static string ComputeSignatureHeader(string payload, string secret, long timestamp)
  {
    var signedPayload = $"{timestamp}.{payload}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
    var v1 = Convert.ToHexString(hash).ToLowerInvariant();
    return $"t={timestamp},v1={v1}";
  }

  [Fact]
  public void ConstructEvent_WithValidSignatureAndCurrentTimestamp_ReturnsEvent()
  {
    // Arrange
    var payload = BuildEventPayload();
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var signature = ComputeSignatureHeader(payload, WebhookSecret, timestamp);

    // Act
    var stripeEvent = _eventUtility.ConstructEvent(payload, signature, WebhookSecret);

    // Assert
    Assert.NotNull(stripeEvent);
    Assert.Equal("evt_sig_test", stripeEvent.Id);
    Assert.Equal("customer.created", stripeEvent.Type);
  }

  [Fact]
  public void ConstructEvent_WithTamperedPayload_ThrowsStripeException()
  {
    // Arrange - sign one payload, then verify a different (tampered) one
    var signedPayload = BuildEventPayload();
    var tamperedPayload = BuildEventPayload(eventId: "evt_attacker_swap");
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var signature = ComputeSignatureHeader(signedPayload, WebhookSecret, timestamp);

    // Act & Assert
    Assert.Throws<StripeException>(
        () => _eventUtility.ConstructEvent(tamperedPayload, signature, WebhookSecret));
  }

  [Fact]
  public void ConstructEvent_WithStaleTimestamp_ThrowsStripeException()
  {
    // Arrange - a correctly-signed payload whose signature timestamp is 600s old, well
    // outside Stripe's default 300s tolerance.
    //
    // This is WHY the internal retry path (WebhookRetryService) must parse the stored
    // payload WITHOUT re-verifying its signature: by the time a retry runs (minutes to
    // hours later), the original signature timestamp is guaranteed to be outside the
    // tolerance window, so re-verification of an already-verified payload would always
    // fail by design.
    var staleTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600;
    var payload = BuildEventPayload(created: staleTimestamp);
    var signature = ComputeSignatureHeader(payload, WebhookSecret, staleTimestamp);

    // Act & Assert
    Assert.Throws<StripeException>(
        () => _eventUtility.ConstructEvent(payload, signature, WebhookSecret));
  }

  [Fact]
  public void ParseEvent_WithOldPayloadAndNoSignature_ReturnsEvent()
  {
    // Arrange - an hour-old payload with no signature involved: proves the replay path
    // used by the retry queue still works long after the signature tolerance has expired
    var oldTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600;
    var payload = BuildEventPayload(eventId: "evt_replay_test", created: oldTimestamp);

    // Act
    var stripeEvent = _eventUtility.ParseEvent(payload);

    // Assert
    Assert.NotNull(stripeEvent);
    Assert.Equal("evt_replay_test", stripeEvent.Id);
    Assert.Equal("customer.created", stripeEvent.Type);
  }
}
