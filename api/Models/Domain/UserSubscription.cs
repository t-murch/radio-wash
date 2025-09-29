namespace RadioWash.Api.Models.Domain;

public static class SubscriptionStatus
{
  public const string Active = "active";
  public const string Canceled = "canceled";
  public const string PastDue = "past_due";
  public const string Trialing = "trialing";
  public const string Incomplete = "incomplete";
}

public class UserSubscription
{
  public int Id { get; set; }
  public int UserId { get; set; }
  public int PlanId { get; set; }
  public string? StripeSubscriptionId { get; set; }
  public string? StripeCustomerId { get; set; }
  public string Status { get; set; } = SubscriptionStatus.Incomplete;
  public DateTime? CurrentPeriodStart { get; set; }
  public DateTime? CurrentPeriodEnd { get; set; }
  public DateTime? CanceledAt { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  public User User { get; set; } = null!;
  public SubscriptionPlan Plan { get; set; } = null!;
}
