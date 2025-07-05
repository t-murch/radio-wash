using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;

namespace RadioWash.Api.Services.Interfaces;

public interface ISpotifyService
{
  Task<SpotifyUserProfile> GetUserProfileAsync(string supabaseUserId);
  Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(string supabaseUserId);
  Task<IEnumerable<SpotifyTrack>> GetPlaylistTracksAsync(string supabaseUserId, string playlistId);
  Task<SpotifyPlaylist> CreatePlaylistAsync(string supabaseUserId, string name, string? description = null);
  Task AddTracksToPlaylistAsync(string supabaseUserId, string playlistId, IEnumerable<string> trackUris);
  Task<SpotifyTrack?> FindCleanVersionAsync(string supabaseUserId, SpotifyTrack explicitTrack);
}
