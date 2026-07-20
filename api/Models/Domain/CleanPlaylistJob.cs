namespace RadioWash.Api.Models.Domain;

public static class JobStatus
{
  public const string Pending = "Pending";
  public const string Processing = "Processing";
  public const string Completed = "Completed";
  public const string Failed = "Failed";
}

public static class JobTypes
{
  /// <summary>Same-service clean: source and target are the same provider.</summary>
  public const string Clean = "clean";
  /// <summary>Cross-service copy: tracks are matched into the target provider's catalog.</summary>
  public const string Copy = "copy";
}

public class CleanPlaylistJob
{
  public int Id { get; set; }

  public int UserId { get; set; }

  public User User { get; set; } = null!;

  /// <summary>
  /// Source music-provider discriminator ("spotify", "apple_music", ...). Used by the
  /// processor to resolve the right IPlaylistCleaner / source IMusicService. Defaults to
  /// "spotify" for existing jobs and for callers that don't specify explicitly.
  /// </summary>
  public string Provider { get; set; } = "spotify";

  /// <summary>
  /// Provider the resulting playlist is created on. Equals <see cref="Provider"/> for
  /// clean jobs; differs for cross-service copy jobs.
  /// </summary>
  public string TargetProvider { get; set; } = "spotify";

  /// <summary>One of <see cref="JobTypes"/>; derived from source/target provider equality.</summary>
  public string JobType { get; set; } = JobTypes.Clean;

  /// <summary>
  /// Whether explicit tracks are swapped for clean versions. Always true for clean jobs;
  /// the per-job toggle for copy jobs (false = faithful 1:1 copy).
  /// </summary>
  public bool SwapExplicitForClean { get; set; } = true;

  public string SourcePlaylistId { get; set; } = null!;
  public string SourcePlaylistName { get; set; } = null!;
  public string? TargetPlaylistId { get; set; }
  public string TargetPlaylistName { get; set; } = null!;
  public string Status { get; set; } = JobStatus.Pending;
  public string? ErrorMessage { get; set; }
  public int TotalTracks { get; set; }
  public int ProcessedTracks { get; set; }
  public int MatchedTracks { get; set; }
  public string? CurrentBatch { get; set; }
  public int? BatchSize { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  public ICollection<TrackMapping> TrackMappings { get; set; } = new List<TrackMapping>();
}
