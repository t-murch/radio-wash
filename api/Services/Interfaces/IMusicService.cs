using RadioWash.Api.Models.Music;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Provider-agnostic surface for a music service (Apple Music, ...). Cleaners and
/// the processor depend on this interface so the same playlist-cleaning logic can drive any
/// supported platform. Provider-specific quirks (URI formats, search query syntax, OAuth
/// flows) live inside each concrete implementation.
/// </summary>
public interface IMusicService
{
  /// <summary>
  /// Identifies which platform this implementation serves (e.g., "apple_music").
  /// Must match the value stored in <c>CleanPlaylistJob.Provider</c> and the key used to
  /// register the service in DI.
  /// </summary>
  string ProviderName { get; }

  Task<MusicUserProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken);

  Task<IReadOnlyList<PlaylistSummary>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken);

  Task<IReadOnlyList<MusicTrack>> GetPlaylistTracksAsync(
      int userId,
      string playlistId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Returns a clean version of the given track, or <c>null</c> when none exists. For
  /// non-explicit tracks, implementations should return the same track unchanged.
  /// </summary>
  Task<MusicTrack?> FindCleanVersionAsync(
      int userId,
      MusicTrack explicitTrack,
      CancellationToken cancellationToken);

  /// <summary>
  /// Looks up tracks on this platform by ISRC. Returns a dictionary keyed by ISRC; ISRCs
  /// with no match on this platform are absent. When multiple platform tracks share an
  /// ISRC, implementations return one representative (callers needing clean/explicit
  /// preference apply it via <see cref="FindCleanVersionAsync"/> or search).
  /// </summary>
  Task<IReadOnlyDictionary<string, MusicTrack>> GetTracksByIsrcAsync(
      int userId,
      IReadOnlyCollection<string> isrcs,
      CancellationToken cancellationToken);

  /// <summary>
  /// Free-text track search on this platform, used as the cross-catalog fallback when no
  /// ISRC match exists.
  /// </summary>
  Task<IReadOnlyList<MusicTrack>> SearchTracksAsync(
      int userId,
      string query,
      int limit,
      CancellationToken cancellationToken);

  Task<PlaylistSummary> CreatePlaylistAsync(
      int userId,
      string name,
      string? description,
      CancellationToken cancellationToken);

  /// <summary>
  /// Adds the given platform-native track IDs to the target playlist. The adapter is
  /// responsible for any platform-specific URI/identifier formatting. Callers pass raw IDs.
  /// </summary>
  Task AddTracksToPlaylistAsync(
      int userId,
      string playlistId,
      IEnumerable<string> trackIds,
      CancellationToken cancellationToken);
}
