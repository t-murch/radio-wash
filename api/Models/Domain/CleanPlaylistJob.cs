namespace RadioWash.Api.Models.Domain;

public static class JobStatus
{
  public const string Pending = "Pending";
  public const string Processing = "Processing";
  public const string Completed = "Completed";
  public const string Failed = "Failed";
}

public class CleanPlaylistJob
{
  public int Id { get; set; }

  public int UserId { get; set; }

  public User User { get; set; } = null!;

  /// <summary>
  /// Music-provider discriminator ("spotify", "apple_music", ...). Used by the processor to
  /// resolve the right IPlaylistCleaner from the factory. Defaults to "spotify" for existing
  /// jobs and for callers that don't specify explicitly.
  /// </summary>
  public string Provider { get; set; } = "spotify";

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
