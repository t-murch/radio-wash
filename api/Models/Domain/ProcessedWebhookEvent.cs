namespace RadioWash.Api.Models.Domain;

public enum WebhookEventStatus
{
  // A handler currently owns this event. A Processing row older than the stale threshold
  // is treated as an abandoned claim (crashed instance) and can be taken over.
  Processing = 0,
  Succeeded = 1,
  // Failed releases the claim: a Stripe redelivery or an internal retry may re-claim the
  // event and attempt processing again.
  Failed = 2
}

public class ProcessedWebhookEvent
{
  public int Id { get; set; }
  public string EventId { get; set; } = null!;
  public string EventType { get; set; } = null!;
  public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
  public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Processing;
  public DateTime? LastAttemptAt { get; set; }
  public int AttemptCount { get; set; } = 1;
  public string? ErrorMessage { get; set; }
}
