namespace RadioWash.Api.Services.Exceptions;

// Thrown when a Stripe webhook payload fails signature verification (tampered payload,
// wrong signing secret, or a timestamp outside Stripe's tolerance window). The controller
// maps this to HTTP 400 so Stripe treats the delivery as permanently rejected; every other
// processing failure returns 500 so Stripe keeps redelivering.
public class WebhookSignatureVerificationException : Exception
{
  public WebhookSignatureVerificationException(string message, Exception innerException)
      : base(message, innerException)
  {
  }
}
