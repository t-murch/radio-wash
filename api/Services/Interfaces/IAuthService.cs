using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;

namespace RadioWash.Api.Services.Interfaces;

public interface IAuthService
{
  string GenerateAuthUrl(string state);
  Task<AuthResponseDto> HandleCallbackAsync(string code);
  Task<User> GetOrCreateUserAsync(SpotifyUserProfile profile, SpotifyAuthResponse tokens);
  Task<string> GenerateJwtTokenAsync(User user);
  Task<bool> ValidateTokenAsync(int userId);
  Task<SpotifyAuthResponse> RefreshTokenAsync(string refreshToken);
}
