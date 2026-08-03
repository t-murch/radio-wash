using RadioWash.Api.Models.Music;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Matches tracks from one platform's catalog into another's. ISRC-first (the recording's
/// cross-catalog identity), free-text search fallback. Stateless: the ISRC index is
/// prefetched once per job and passed into each per-track match.
/// </summary>
public interface ITrackMatcher
{
  /// <summary>Batch-resolves the given ISRCs on the target platform.</summary>
  Task<IReadOnlyDictionary<string, MusicTrack>> PrefetchByIsrcAsync(
      int userId,
      IMusicService target,
      IReadOnlyCollection<string> isrcs,
      CancellationToken cancellationToken);

  /// <summary>
  /// Matches one source track into the target platform. With <paramref name="preferClean"/>,
  /// explicit matches are swapped for clean versions and tracks with no clean version are
  /// left unmatched (mirroring the same-service clean semantics); without it, the match is a
  /// faithful copy.
  /// </summary>
  Task<TrackMatch> MatchAsync(
      int userId,
      IMusicService target,
      MusicTrack source,
      IReadOnlyDictionary<string, MusicTrack> isrcIndex,
      bool preferClean,
      CancellationToken cancellationToken);
}
