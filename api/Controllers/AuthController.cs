using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RadioWash.Api.Configuration;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;
using SpotifyAPI.Web;

namespace RadioWash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
  // Spotify access tokens expire in 3600 seconds.
  private const int SpotifyExpiresInSeconds = 3600;

  private readonly ILogger<AuthController> _logger;
  private readonly IMemoryCache _memoryCache;
  private readonly IConfiguration _configuration;
  private readonly IWebHostEnvironment _environment;
  private readonly IUserService _userService;
  private readonly IMusicTokenService _musicTokenService;
  private readonly IAppleDeveloperTokenProvider _appleDeveloperTokenProvider;
  private readonly AppleMusicSettings _appleMusicSettings;

  public AuthController(
      ILogger<AuthController> logger,
      IMemoryCache memoryCache,
      IConfiguration configuration,
      IWebHostEnvironment environment,
      IUserService userService,
      IMusicTokenService musicTokenService,
      IAppleDeveloperTokenProvider appleDeveloperTokenProvider,
      IOptions<AppleMusicSettings> appleMusicSettings)
  {
    _logger = logger;
    _memoryCache = memoryCache;
    _configuration = configuration;
    _environment = environment;
    _userService = userService;
    _musicTokenService = musicTokenService;
    _appleDeveloperTokenProvider = appleDeveloperTokenProvider;
    _appleMusicSettings = appleMusicSettings.Value;
  }

  // Apple Music user tokens are long-lived (~6 months) with no expiry signal; store them
  // with the configured assumed lifetime so reconnect prompts fire before they lapse.
  private int ExpiresInSecondsFor(string provider) => provider switch
  {
    MusicProviders.AppleMusic => _appleMusicSettings.UserTokenAssumedLifetimeDays * 24 * 3600,
    _ => SpotifyExpiresInSeconds
  };


  /// <summary>
  /// Stores OAuth tokens for a music provider received from the frontend OAuth callback.
  /// Replaces the provider-specific <c>/spotify/tokens</c> route; that route remains as an
  /// <c>[Obsolete]</c> alias until the frontend migrates.
  /// </summary>
  [HttpPost("tokens/{provider}")]
  [Authorize]
  public async Task<IActionResult> StoreTokens(string provider, [FromBody] SpotifyTokenRequest request)
  {
    if (!MusicProviders.TryNormalize(provider, out var normalizedProvider))
    {
      return BadRequest(new { error = $"Provider '{provider}' is not supported." });
    }

    try
    {
      var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
      if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
      {
        return Unauthorized(new { error = "User ID not found in token." });
      }

      var user = await _userService.GetUserBySupabaseIdAsync(userId);
      if (user == null)
      {
        return NotFound(new { error = "User not found." });
      }

      var scopes = ScopesForProvider(normalizedProvider);

      await _musicTokenService.StoreTokensAsync(
        user.Id,
        normalizedProvider,
        request.AccessToken,
        request.RefreshToken,
        ExpiresInSecondsFor(normalizedProvider),
        scopes,
        null);

      _logger.LogInformation("Successfully stored {Provider} tokens for user {UserId}", normalizedProvider, user.Id);
      return Ok(new { success = true });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error storing {Provider} tokens", normalizedProvider);
      return StatusCode(500, new { error = $"Failed to store {normalizedProvider} tokens" });
    }
  }

  /// <summary>
  /// Gets the connection status for the given music provider for the authenticated user.
  /// </summary>
  [HttpGet("status/{provider}")]
  [Authorize]
  public async Task<IActionResult> ConnectionStatus(string provider)
  {
    if (!MusicProviders.TryNormalize(provider, out var normalizedProvider))
    {
      return BadRequest(new { error = $"Provider '{provider}' is not supported." });
    }

    try
    {
      var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
      if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
      {
        return Unauthorized(new { error = "User ID not found in token." });
      }

      var user = await _userService.GetUserBySupabaseIdAsync(userId);
      if (user == null)
      {
        return NotFound(new { error = "User not found." });
      }

      var hasValidTokens = await _musicTokenService.HasValidTokensAsync(user.Id, normalizedProvider);
      var tokenInfo = await _musicTokenService.GetTokenInfoAsync(user.Id, normalizedProvider);

      return Ok(new
      {
        provider = normalizedProvider,
        connected = hasValidTokens,
        connectedAt = tokenInfo?.CreatedAt,
        lastRefreshAt = tokenInfo?.LastRefreshAt,
        canRefresh = tokenInfo?.CanRefresh ?? false,
        // Drives the frontend's reconnect prompt. Meaningful for providers without a
        // refresh flow (Apple Music), where canRefresh is always false.
        expiresAt = tokenInfo?.ExpiresAt
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting {Provider} connection status", normalizedProvider);
      return StatusCode(500, new { error = "Failed to get connection status" });
    }
  }

  /// <summary>
  /// Disconnects a music provider by deleting the tokens stored for the authenticated user.
  /// This is the extent of what the server can do: neither provider supports revoking the
  /// credential itself from here — Spotify has no OAuth revocation endpoint (users remove
  /// the app at spotify.com/account/apps) and Apple Music User Tokens are managed in Apple's
  /// settings. The frontend additionally drops the browser-side MusicKit grant for Apple.
  /// Idempotent: disconnecting an already-disconnected provider succeeds.
  /// </summary>
  [HttpDelete("tokens/{provider}")]
  [Authorize]
  public async Task<IActionResult> DisconnectProvider(string provider)
  {
    if (!MusicProviders.TryNormalize(provider, out var normalizedProvider))
    {
      return BadRequest(new { error = $"Provider '{provider}' is not supported." });
    }

    try
    {
      var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
      if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
      {
        return Unauthorized(new { error = "User ID not found in token." });
      }

      var user = await _userService.GetUserBySupabaseIdAsync(userId);
      if (user == null)
      {
        return NotFound(new { error = "User not found." });
      }

      await _musicTokenService.RevokeTokensAsync(user.Id, normalizedProvider);

      _logger.LogInformation("Disconnected {Provider} for user {UserId}", normalizedProvider, user.Id);
      return Ok(new { success = true });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error disconnecting {Provider}", normalizedProvider);
      return StatusCode(500, new { error = $"Failed to disconnect {normalizedProvider}" });
    }
  }

  /// <summary>
  /// Returns the MusicKit developer token so the frontend can configure MusicKit JS and
  /// start Apple Music user authorization. The token is an app credential (not per-user
  /// secret) but is only needed inside the authenticated dashboard, so it stays gated.
  /// </summary>
  [HttpGet("musickit/devtoken")]
  [Authorize]
  public async Task<IActionResult> MusicKitDeveloperToken()
  {
    if (!_appleDeveloperTokenProvider.IsConfigured)
    {
      return StatusCode(503, new { error = "apple_music_not_configured", message = "Apple Music is not configured on this server." });
    }

    try
    {
      var token = await _appleDeveloperTokenProvider.GetDeveloperTokenAsync(cancellationToken: HttpContext.RequestAborted);
      return Ok(new { token });
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogError(ex, "Apple Music developer token generation failed");
      return StatusCode(503, new { error = "apple_music_not_configured", message = "Apple Music is not configured on this server." });
    }
  }

  private static string[] ScopesForProvider(string provider) => provider.ToLowerInvariant() switch
  {
    MusicProviders.Spotify => new[]
    {
      "user-read-private", "user-read-email", "playlist-read-private",
      "playlist-read-collaborative", "playlist-modify-public", "playlist-modify-private"
    },
    // Apple Music user tokens are scope-less; authorization is all-or-nothing via MusicKit.
    MusicProviders.AppleMusic => Array.Empty<string>(),
    _ => Array.Empty<string>()
  };

  /// <summary>
  /// Gets the profile of the currently authenticated user.
  /// </summary>
  [HttpGet("me")]
  [Authorize]
  public async Task<IActionResult> Me()
  {
    _logger.LogInformation("Getting authenticated user.");
    try
    {
      var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
      if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
      {
        return Unauthorized(new { error = "User ID not found in token." });
      }

      var user = await _userService.GetUserBySupabaseIdAsync(userId);
      if (user == null)
      {
        return NotFound(new { error = "User not found." });
      }

      return Ok(user);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting authenticated user.");
      return StatusCode(500, new { error = "Failed to get user profile." });
    }
  }

  /// <summary>
  /// Logs the user out by ending Supabase session. Optionally revokes music service tokens.
  /// </summary>
  /// <param name="revokeTokens">If true, also revokes stored music service tokens (for shared devices)</param>
  [HttpPost("logout")]
  [Authorize]
  public async Task<IActionResult> Logout([FromQuery] bool revokeTokens = false)
  {
    try
    {
      var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
      var tokensRevoked = false;

      // Optionally revoke music service tokens (for shared devices or security concerns)
      if (revokeTokens && userIdClaim != null && Guid.TryParse(userIdClaim, out var userId))
      {
        var user = await _userService.GetUserBySupabaseIdAsync(userId);
        if (user != null)
        {
          // Every provider, not a hardcoded list — this path previously only revoked
          // Spotify and silently left Apple Music connected on shared devices.
          foreach (var provider in MusicProviders.All)
          {
            await _musicTokenService.RevokeTokensAsync(user.Id, provider);
          }
          tokensRevoked = true;
          _logger.LogInformation("Revoked music tokens for user {UserId} (explicit request)", user.Id);
        }
      }

      // Supabase session is handled by the frontend

      _logger.LogInformation("User logged out successfully. Tokens revoked: {TokensRevoked}", tokensRevoked);

      return Ok(new
      {
        success = true,
        tokensRevoked,
        message = tokensRevoked
          ? "Logged out and revoked music service connections"
          : "Logged out successfully. Music service connections preserved."
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error during logout");
      return StatusCode(500, new { error = "Failed to logout" });
    }
  }



  private string GetFrontendUrl()
  {
    return _configuration["FrontendUrl"] ?? "http://127.0.0.1:3000";
  }

  private string GetBackendUrl()
  {
    return _configuration["BackendUrl"] ?? "http://127.0.0.1:5159";
  }
}
