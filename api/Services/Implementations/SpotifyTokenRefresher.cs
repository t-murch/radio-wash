using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using SpotifyAPI.Web;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Spotify-specific implementation of <see cref="IMusicTokenRefresher"/>. Extracted from
/// <see cref="MusicTokenService"/> so the token-service doesn't carry a per-provider switch
/// and Apple Music can register its own refresher (or skip registering one, since Apple's
/// user tokens don't have a refresh flow).
/// </summary>
public class SpotifyTokenRefresher : IMusicTokenRefresher
{
  public const string Provider = "spotify";

  private readonly IUserMusicTokenRepository _tokenRepository;
  private readonly ITokenEncryptionService _encryptionService;
  private readonly IConfiguration _configuration;
  private readonly ILogger<SpotifyTokenRefresher> _logger;

  public SpotifyTokenRefresher(
    IUserMusicTokenRepository tokenRepository,
    ITokenEncryptionService encryptionService,
    IConfiguration configuration,
    ILogger<SpotifyTokenRefresher> logger)
  {
    _tokenRepository = tokenRepository;
    _encryptionService = encryptionService;
    _configuration = configuration;
    _logger = logger;
  }

  public string ProviderName => Provider;

  public async Task<bool> RefreshAsync(UserMusicToken token, CancellationToken cancellationToken)
  {
    if (token.EncryptedRefreshToken == null)
    {
      _logger.LogWarning("No refresh token available for user {UserId}", token.UserId);
      return false;
    }

    var clientId = _configuration["Spotify:ClientId"];
    var clientSecret = _configuration["Spotify:ClientSecret"];
    var refreshToken = _encryptionService.DecryptToken(token.EncryptedRefreshToken);

    try
    {
      var request = new AuthorizationCodeRefreshRequest(clientId!, clientSecret!, refreshToken);
      var response = await new OAuthClient().RequestToken(request);

      if (response.AccessToken != null)
      {
        token.EncryptedAccessToken = _encryptionService.EncryptToken(response.AccessToken);
        token.ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn);

        // Spotify may rotate the refresh token; when it does, persist the new one.
        if (!string.IsNullOrEmpty(response.RefreshToken))
        {
          token.EncryptedRefreshToken = _encryptionService.EncryptToken(response.RefreshToken);
        }

        token.MarkRefreshSuccess();
        await _tokenRepository.UpdateAsync(token);

        _logger.LogInformation("Successfully refreshed Spotify token for user {UserId}", token.UserId);
        return true;
      }

      token.MarkRefreshFailure();
      await _tokenRepository.UpdateAsync(token);
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to refresh Spotify token for user {UserId}", token.UserId);
      token.MarkRefreshFailure();
      await _tokenRepository.UpdateAsync(token);
      return false;
    }
  }
}
