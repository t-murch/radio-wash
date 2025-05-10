namespace RadioWash.Api.Models.Domain;

public class TrackMapping
{
  public int Id { get; set; }
  public int JobId { get; set; }
  public string SourceTrackId { get; set; } = null!;
  public string SourceTrackName { get; set; } = null!;
  public string SourceArtistName { get; set; } = null!;
  public bool IsExplicit { get; set; }
  public string? TargetTrackId { get; set; }
  public string? TargetTrackName { get; set; }
  public string? TargetArtistName { get; set; }
  public bool HasCleanMatch { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  public CleanPlaylistJob Job { get; set; } = null!;
}
