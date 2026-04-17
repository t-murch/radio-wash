using RadioWash.Api.Models.Music;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Provider-agnostic surface for a music service (Spotify, Apple Music, ...). Cleaners and
/// the processor depend on this interface so the same playlist-cleaning logic can drive any
/// supported platform. Provider-specific quirks (URI formats, search query syntax, OAuth
/// flows) live inside each concrete implementation.
/// </summary>
public interface IMusicService
{
  /// <summary>
  /// Identifies which platform this implementation serves (e.g., "spotify", "apple_music").
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

  Task<PlaylistSummary> CreatePlaylistAsync(
      int userId,
      string name,
      string? description,
      CancellationToken cancellationToken);

  /// <summary>
  /// Adds the given platform-native track IDs to the target playlist. The adapter is
  /// responsible for any platform-specific URI/identifier formatting (e.g., Spotify's
  /// <c>spotify:track:&lt;id&gt;</c>). Callers pass raw IDs.
  /// </summary>
  Task AddTracksToPlaylistAsync(
      int userId,
      string playlistId,
      IEnumerable<string> trackIds,
      CancellationToken cancellationToken);
}
