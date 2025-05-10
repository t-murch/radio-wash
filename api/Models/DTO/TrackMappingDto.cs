namespace RadioWash.Api.Models.DTO;

public class TrackMappingDto
{
  public int Id { get; set; }
  public string SourceTrackId { get; set; } = null!;
  public string SourceTrackName { get; set; } = null!;
  public string SourceArtistName { get; set; } = null!;
  public bool IsExplicit { get; set; }
  public string? TargetTrackId { get; set; }
  public string? TargetTrackName { get; set; }
  public string? TargetArtistName { get; set; }
  public bool HasCleanMatch { get; set; }
}
