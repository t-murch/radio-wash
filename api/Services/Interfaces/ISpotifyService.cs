using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;

namespace RadioWash.Api.Services.Interfaces;

public interface ISpotifyService
{
  Task<SpotifyUserProfile> GetUserProfileAsync(string accessToken);
  Task<List<PlaylistDto>> GetUserPlaylistsAsync(int userId);
  Task<List<SpotifyPlaylistTrack>> GetPlaylistTracksAsync(int userId, string playlistId);
  Task<string> CreatePlaylistAsync(int userId, string name, string? description = null);
  Task AddTracksToPlaylistAsync(int userId, string playlistId, List<string> trackUris);
  Task<List<SpotifyTrack>> SearchTracksAsync(int userId, string query, int limit = 5);
}
