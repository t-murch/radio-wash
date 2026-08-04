namespace RadioWash.Api.Models.Domain;

public enum WebhookRetryStatus
{
  Pending = 0,
  Processing = 1,
  Succeeded = 2,
  Failed = 3,
  MaxRetriesExceeded = 4,
  // Another delivery path (Stripe redelivery, or a concurrent handler) processed the event
  // before this retry ran — distinct from Succeeded so ops data doesn't claim this retry
  // did the work.
  Superseded = 5
}

public class WebhookRetry
{
  public int Id { get; set; }
  public string EventId { get; set; } = null!;
  public string EventType { get; set; } = null!;
  public string Payload { get; set; } = null!;
  public string Signature { get; set; } = null!;
  public int AttemptNumber { get; set; } = 1;
  public int MaxRetries { get; set; } = 5;
  public WebhookRetryStatus Status { get; set; } = WebhookRetryStatus.Pending;
  public DateTime NextRetryAt { get; set; } = DateTime.UtcNow;
  public string? LastErrorMessage { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}