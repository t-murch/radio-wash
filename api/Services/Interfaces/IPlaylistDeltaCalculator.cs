using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;

namespace RadioWash.Api.Services.Interfaces;

public class PlaylistDelta
{
  public List<string> TracksToAdd { get; set; } = new();

  /// <summary>
  /// Tracks present in the target but no longer in the source. Retained because it is
  /// meaningful reporting — it tells a user their clean copy has drifted — but note that
  /// sync does not act on it: Apple Music's API cannot remove tracks from a library
  /// playlist. See <c>PlaylistSyncService.ApplyDeltaToPlaylistAsync</c>.
  /// </summary>
  public List<string> TracksToRemove { get; set; } = new();

  public List<MusicTrack> NewTracks { get; set; } = new();
  public List<string> DesiredTrackOrder { get; set; } = new();
}

public interface IPlaylistDeltaCalculator
{
  Task<PlaylistDelta> CalculateDeltaAsync(
      List<MusicTrack> sourceTracks,
      List<MusicTrack> targetTracks,
      List<TrackMapping> existingMappings);
}
