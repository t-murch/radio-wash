namespace RadioWash.Api.Models.DTO;

public class CleanPlaylistJobDto
{
  public int Id { get; set; }
  public string SourcePlaylistId { get; set; } = null!;
  public string SourcePlaylistName { get; set; } = null!;
  public string? TargetPlaylistId { get; set; }
  public string? TargetPlaylistName { get; set; }
  public string Status { get; set; } = null!;
  public string? ErrorMessage { get; set; }
  public int TotalTracks { get; set; }
  public int ProcessedTracks { get; set; }
  public int MatchedTracks { get; set; }
  public string? CurrentBatch { get; set; }
  public int? BatchSize { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}
