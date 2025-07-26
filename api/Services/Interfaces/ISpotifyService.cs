using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;

namespace RadioWash.Api.Services.Interfaces;

public interface ISpotifyService
{
  Task<SpotifyUserProfile> GetUserProfileAsync(int userId);
  Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(int userId);
  Task<IEnumerable<SpotifyTrack>> GetPlaylistTracksAsync(int userId, string playlistId);
  Task<SpotifyPlaylist> CreatePlaylistAsync(int userId, string name, string? description = null);
  Task AddTracksToPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackUris);
  Task<SpotifyTrack?> FindCleanVersionAsync(int userId, SpotifyTrack explicitTrack);
}
