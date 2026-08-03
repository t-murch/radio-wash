using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;

namespace RadioWash.Api.Services.Interfaces;

public interface ISpotifyService
{
  Task<SpotifyUserProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken = default);
  Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken = default);
  Task<IEnumerable<SpotifyTrack>> GetPlaylistTracksAsync(int userId, string playlistId, CancellationToken cancellationToken = default);
  Task<SpotifyPlaylist> CreatePlaylistAsync(int userId, string name, string? description = null, CancellationToken cancellationToken = default);
  Task AddTracksToPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackUris, CancellationToken cancellationToken = default);
  Task RemoveTracksFromPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackUris, CancellationToken cancellationToken = default);
  Task<SpotifyTrack?> FindCleanVersionAsync(int userId, SpotifyTrack explicitTrack, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<SpotifyTrack>> SearchTracksAsync(int userId, string query, int limit, CancellationToken cancellationToken = default);
  /// <summary>Looks up one track per ISRC via Spotify search (no batch endpoint exists).</summary>
  Task<SpotifyTrack?> GetTrackByIsrcAsync(int userId, string isrc, CancellationToken cancellationToken = default);
}
