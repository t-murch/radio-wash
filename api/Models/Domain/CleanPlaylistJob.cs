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
