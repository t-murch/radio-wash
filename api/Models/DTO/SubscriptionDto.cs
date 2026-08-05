namespace RadioWash.Api.Models.DTO;

public class SubscriptionPlanDto
{
  public int Id { get; set; }
  public string Name { get; set; } = null!;
  public decimal Price { get; set; }
  public string BillingPeriod { get; set; } = null!;
  public string? StripePriceId { get; set; }
  public int? MaxPlaylists { get; set; }
  public int? MaxTracksPerPlaylist { get; set; }
  public List<string> Features { get; set; } = new();
  public bool IsActive { get; set; }
}

public class UserSubscriptionDto
{
  public int Id { get; set; }
  public string Status { get; set; } = null!;
  public DateTime? CurrentPeriodStart { get; set; }
  public DateTime? CurrentPeriodEnd { get; set; }
  public DateTime? CanceledAt { get; set; }
  public bool CancelAtPeriodEnd { get; set; }
  public SubscriptionPlanDto Plan { get; set; } = null!;
  public DateTime CreatedAt { get; set; }
}

public class CreateCheckoutDto
{
  // Local plan id; null selects the single active plan. The Stripe price is always
  // resolved server-side — client-supplied price ids are never sent to Stripe.
  public int? PlanId { get; set; }
  // Client-generated GUID per checkout click; feeds Stripe's idempotency key so a retried
  // request can't create duplicate checkout sessions.
  public string? ClientRequestId { get; set; }
}

public class CompleteCheckoutDto
{
  public string SessionId { get; set; } = null!;
}

public class PlaylistSyncConfigDto
{
  public int Id { get; set; }
  public int OriginalJobId { get; set; }
  public string SourcePlaylistId { get; set; } = null!;
  public string SourcePlaylistName { get; set; } = null!;
  public string TargetPlaylistId { get; set; } = null!;
  public string TargetPlaylistName { get; set; } = null!;
  public bool IsActive { get; set; }
  public string SyncFrequency { get; set; } = null!;
  public DateTime? LastSyncedAt { get; set; }
  public string? LastSyncStatus { get; set; }
  public string? LastSyncError { get; set; }
  public DateTime? NextScheduledSync { get; set; }
  public DateTime CreatedAt { get; set; }
}

public class PlaylistSyncHistoryDto
{
  public int Id { get; set; }
  public DateTime StartedAt { get; set; }
  public DateTime? CompletedAt { get; set; }
  public string Status { get; set; } = null!;
  public int TracksAdded { get; set; }
  public int TracksRemoved { get; set; }
  public int TracksUnchanged { get; set; }
  public string? ErrorMessage { get; set; }
  public int? ExecutionTimeMs { get; set; }
}

public class EnableSyncDto
{
  public int JobId { get; set; }
}

public class UpdateSyncFrequencyDto
{
  public string Frequency { get; set; } = null!;
}

public class SyncResultDto
{
  public bool Success { get; set; }
  public int TracksAdded { get; set; }
  public int TracksRemoved { get; set; }
  public int TracksUnchanged { get; set; }
  public string? ErrorMessage { get; set; }
  public long ExecutionTimeMs { get; set; }
}
