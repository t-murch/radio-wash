namespace RadioWash.Api.Models.Domain;

public static class ContactEmailStatus
{
  public const string Pending = "Pending";
  public const string Sent = "Sent";
  public const string Failed = "Failed";
}

/// <summary>
/// A contact-form submission. The row is the durable record: it is written before any email
/// is attempted, so a delivery failure never loses the message. No FK to Users — anonymous
/// visitors submit too, and a signed-in submitter may not have a local Users row.
/// </summary>
public class ContactSubmission
{
  public int Id { get; set; }
  public string Name { get; set; } = null!;
  public string Email { get; set; } = null!;
  public string Message { get; set; } = null!;

  // Supabase `sub` claim when a bearer token accompanied the request; null for anonymous.
  public string? SupabaseUserId { get; set; }
  public string? ClientIp { get; set; }

  public string EmailStatus { get; set; } = ContactEmailStatus.Pending;
  public DateTime? EmailSentAt { get; set; }
  public string? EmailError { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
