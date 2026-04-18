namespace RadioWash.Api.Models.DTO;

public class CreateCleanPlaylistJobDto
{
  public string SourcePlaylistId { get; set; } = null!;
  public string? TargetPlaylistName { get; set; }

  /// <summary>
  /// Optional music-provider identifier ("spotify", "apple_music"). Omit to accept the server
  /// default of "spotify" — current frontend omits this field and must keep working.
  /// </summary>
  public string? Provider { get; set; }
}
