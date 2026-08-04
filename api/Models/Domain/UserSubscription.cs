namespace RadioWash.Api.Models.Domain;

public static class SubscriptionStatus
{
  public const string Active = "active";
  public const string Canceled = "canceled";
  public const string PastDue = "past_due";
  public const string Trialing = "trialing";
  public const string Incomplete = "incomplete";
  public const string Unpaid = "unpaid";
  public const string Paused = "paused";
}

// Normalizes Stripe subscription statuses into the local vocabulary. Stripe can emit
// statuses the app has no distinct behavior for (incomplete_expired); those collapse into
// the closest local status instead of being written verbatim, so entitlement checks that
// enumerate statuses stay exhaustive.
public static class SubscriptionStatusMapper
{
  private static readonly HashSet<string> KnownStatuses = new(StringComparer.Ordinal)
  {
    SubscriptionStatus.Active,
    SubscriptionStatus.Canceled,
    SubscriptionStatus.PastDue,
    SubscriptionStatus.Trialing,
    SubscriptionStatus.Incomplete,
    SubscriptionStatus.Unpaid,
    SubscriptionStatus.Paused,
  };

  // Statuses that grant access to paid features.
  public static bool IsEntitled(string? status) =>
      status == SubscriptionStatus.Active || status == SubscriptionStatus.Trialing;

  // Statuses under which sync configs must not keep running.
  public static bool IsInactive(string? status) =>
      status == SubscriptionStatus.Canceled
      || status == SubscriptionStatus.PastDue
      || status == SubscriptionStatus.Incomplete
      || status == SubscriptionStatus.Unpaid
      || status == SubscriptionStatus.Paused;

  /// <summary>
  /// Maps a raw Stripe status to the local status vocabulary. Returns false when the
  /// status is unknown; the raw value is still returned so callers can persist it
  /// (never lose Stripe's state) while logging the gap.
  /// </summary>
  public static bool TryMap(string rawStripeStatus, out string mapped)
  {
    if (rawStripeStatus == "incomplete_expired")
    {
      mapped = SubscriptionStatus.Canceled;
      return true;
    }

    mapped = rawStripeStatus;
    return KnownStatuses.Contains(rawStripeStatus);
  }
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
  // Mirrors Stripe's cancel_at_period_end: the user requested cancellation but keeps access
  // until CurrentPeriodEnd; the customer.subscription.deleted webhook performs the real
  // deactivation. Cleared automatically if the user resumes via the billing portal.
  public bool CancelAtPeriodEnd { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  public User User { get; set; } = null!;
  public SubscriptionPlan Plan { get; set; } = null!;
}
