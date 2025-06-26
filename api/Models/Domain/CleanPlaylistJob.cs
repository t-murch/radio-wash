namespace RadioWash.Api.Models.Domain;

public enum JobStatus
{
  Created,
  Processing,
  Completed,
  Failed
}

public class CleanPlaylistJob
{
  public int Id { get; set; }
  public Guid UserId { get; set; }
  public string SourcePlaylistId { get; set; } = null!;
  public string SourcePlaylistName { get; set; } = null!;
  public string? TargetPlaylistId { get; set; }
  public string? TargetPlaylistName { get; set; }
  public JobStatus Status { get; set; } = JobStatus.Created;
  public string? ErrorMessage { get; set; }
  public int TotalTracks { get; set; }
  public int ProcessedTracks { get; set; }
  public int MatchedTracks { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  public User User { get; set; } = null!;
  public ICollection<TrackMapping> TrackMappings { get; set; } = new List<TrackMapping>();
}
