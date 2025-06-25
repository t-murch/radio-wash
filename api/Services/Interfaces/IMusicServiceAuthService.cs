using RadioWash.Api.Controllers;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

public interface IMusicServiceAuthService
{
    Task<IEnumerable<UserMusicService>> GetConnectedServicesAsync(Guid supabaseUserId);
    string GenerateSpotifyAuthUrl(string state);
    Task HandleSpotifyCallbackAsync(Guid supabaseUserId, string code);
    string GenerateAppleMusicAuthUrl(string state);
    Task HandleAppleMusicCallbackAsync(Guid supabaseUserId, string code);
    Task DisconnectServiceAsync(Guid supabaseUserId, MusicServiceType serviceType);
    Task<string?> GetValidTokenAsync(Guid supabaseUserId, MusicServiceType serviceType);
}