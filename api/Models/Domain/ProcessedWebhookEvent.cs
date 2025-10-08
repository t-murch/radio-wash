namespace RadioWash.Api.Models.Domain;

public class ProcessedWebhookEvent
{
  public int Id { get; set; }
  public string EventId { get; set; } = null!;
  public string EventType { get; set; } = null!;
  public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
  public bool IsSuccessful { get; set; } = true;
  public string? ErrorMessage { get; set; }
}