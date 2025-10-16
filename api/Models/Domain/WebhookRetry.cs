namespace RadioWash.Api.Models.Domain;

public class WebhookRetry
{
    public int Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public int MaxRetries { get; set; } = 3;
    public DateTime NextRetryAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? LastErrorMessage { get; set; }
    public WebhookRetryStatus Status { get; set; } = WebhookRetryStatus.Pending;
}

public enum WebhookRetryStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Exhausted
}