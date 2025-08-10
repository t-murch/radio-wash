using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
  private readonly ILogger<AuthController> _logger;
  private readonly IMemoryCache _memoryCache;
  private readonly IConfiguration _configuration;
  private readonly IWebHostEnvironment _environment;
  private readonly IUserService _userService;
  private readonly IMusicTokenService _musicTokenService;

  public AuthController(
      ILogger<AuthController> logger,
      IMemoryCache memoryCache,
      IConfiguration configuration,
      IWebHostEnvironment environment,
      IUserService userService,
      IMusicTokenService musicTokenService)
  {
    _logger = logger;
    _memoryCache = memoryCache;
    _configuration = configuration;
    _environment = environment;
    _userService = userService;
    _musicTokenService = musicTokenService;
  }


  /// <summary>
  /// Stores Spotify tokens received from the frontend OAuth callback
  /// </summary>
  [HttpPost("spotify/tokens")]
  [Authorize]
  public async Task<IActionResult> StoreSpotifyTokens([FromBody] SpotifyTokenRequest request)
  {
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

      var scopes = new[] {
        "user-read-private", "user-read-email", "playlist-read-private",
        "playlist-read-collaborative", "playlist-modify-public", "playlist-modify-private"
      };

      await _musicTokenService.StoreTokensAsync(
        user.Id,
        "spotify",
        request.AccessToken,
        request.RefreshToken,
        3600, // Spotify tokens expire in 1 hour
        scopes,
        null // No additional metadata needed from token sync
      );

      _logger.LogInformation("Successfully stored Spotify tokens for user {UserId}", user.Id);
      return Ok(new { success = true });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error storing Spotify tokens");
      return StatusCode(500, new { error = "Failed to store Spotify tokens" });
    }
  }

  /// <summary>
  /// Gets Spotify connection status for the authenticated user.
  /// </summary>
  [HttpGet("spotify/status")]
  [Authorize]
  public async Task<IActionResult> SpotifyConnectionStatus()
  {
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

      var hasValidTokens = await _musicTokenService.HasValidTokensAsync(user.Id, "spotify");
      var tokenInfo = await _musicTokenService.GetTokenInfoAsync(user.Id, "spotify");

      return Ok(new
      {
        connected = hasValidTokens,
        connectedAt = tokenInfo?.CreatedAt,
        lastRefreshAt = tokenInfo?.LastRefreshAt,
        canRefresh = tokenInfo?.CanRefresh ?? false
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting Spotify connection status");
      return StatusCode(500, new { error = "Failed to get connection status" });
    }
  }

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
          await _musicTokenService.RevokeTokensAsync(user.Id, "spotify");
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
