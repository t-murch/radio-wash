namespace RadioWash.Api.Models.DTO;

public class CreateCleanPlaylistJobDto
{
  public string SourcePlaylistId { get; set; } = null!;
  public string? TargetPlaylistName { get; set; }

  /// <summary>
  /// Optional source music-provider identifier ("spotify", "apple_music"). Omit to accept
  /// the server default of "spotify" — current frontend omits this field and must keep working.
  /// </summary>
  public string? Provider { get; set; }

  /// <summary>
  /// Optional target provider. Omit (or equal to <see cref="Provider"/>) for a same-service
  /// clean job; a different value makes this a cross-service copy job.
  /// </summary>
  public string? TargetProvider { get; set; }

  /// <summary>
  /// Copy jobs only: swap explicit tracks for clean versions during the transfer (default
  /// true). Clean jobs always swap.
  /// </summary>
  public bool? SwapExplicitForClean { get; set; }
}
