using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using RadioWash.Api.Configuration;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class TokenService : ITokenService
{
  private readonly RadioWashDbContext _dbContext;
  private readonly HttpClient _httpClient;
  private readonly SpotifySettings _spotifySettings;

  public TokenService(RadioWashDbContext dbContext, HttpClient httpClient, IOptions<SpotifySettings> spotifySettings)
  {
    _dbContext = dbContext;
    _httpClient = httpClient;
    _spotifySettings = spotifySettings.Value;
  }

  public async Task<string> GetAccessTokenAsync(int userId)
  {
    var token = await GetUserTokenAsync(userId);

    if (token == null)
    {
      throw new Exception("User token not found");
    }

    // Check if token is expired or will expire in the next 5 minutes
    if (token.ExpiresAt <= DateTime.UtcNow.AddMinutes(5))
    {
      return await RefreshTokenAsync(userId);
    }

    return token.AccessToken;
  }

  public async Task<UserToken> GetUserTokenAsync(int userId)
  {
    var token = await _dbContext.UserTokens
        .FirstOrDefaultAsync(t => t.UserId == userId);

    return token;
  }

  public async Task UpdateTokenAsync(UserToken token, string accessToken, string refreshToken, int expiresIn)
  {
    token.AccessToken = accessToken;
    token.RefreshToken = refreshToken;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
    token.UpdatedAt = DateTime.UtcNow;

    _dbContext.UserTokens.Update(token);
    await _dbContext.SaveChangesAsync();
  }

  public async Task<string> RefreshTokenAsync(int userId)
  {
    var token = await GetUserTokenAsync(userId);

    if (token == null || string.IsNullOrEmpty(token.RefreshToken))
    {
      throw new Exception("Refresh token not found");
    }

    var request = new HttpRequestMessage(HttpMethod.Post, _spotifySettings.TokenUrl);

    var content = new FormUrlEncodedContent(new Dictionary<string, string>
    {
      { "grant_type", "refresh_token" },
      { "refresh_token", token.RefreshToken },
      { "client_id", _spotifySettings.ClientId },
      { "client_secret", _spotifySettings.ClientSecret }
    });

    request.Content = content;

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    var tokenResponse = JsonSerializer.Deserialize<SpotifyAuthResponse>(
      jsonResponse,
      new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    if (tokenResponse == null)
    {
      throw new Exception("Failed to deserialize refresh token response");
    }

    // Update the token in database
    await UpdateTokenAsync(
      token,
      tokenResponse.AccessToken,
      tokenResponse.RefreshToken ?? token.RefreshToken, // Some refresh responses don't include new refresh token
      tokenResponse.ExpiresIn
    );

    return tokenResponse.AccessToken;
  }
}
