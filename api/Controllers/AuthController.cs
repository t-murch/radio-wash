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
using Supabase.Gotrue;

namespace RadioWash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
  private readonly ILogger<AuthController> _logger;
  private readonly IMemoryCache _memoryCache;
  private readonly IConfiguration _configuration;
  private readonly IWebHostEnvironment _environment;
  private readonly Supabase.Gotrue.Client _supabaseAuth;
  private readonly IUserService _userService;
  private readonly IMusicTokenService _musicTokenService;

  public AuthController(
      ILogger<AuthController> logger,
      IMemoryCache memoryCache,
      IConfiguration configuration,
      IWebHostEnvironment environment,
      Supabase.Gotrue.Client supabaseAuth,
      IUserService userService,
      IMusicTokenService musicTokenService)
  {
    _logger = logger;
    _memoryCache = memoryCache;
    _configuration = configuration;
    _environment = environment;
    _supabaseAuth = supabaseAuth;
    _userService = userService;
    _musicTokenService = musicTokenService;
  }

  /// <summary>
  /// Generates a Spotify authorization URL and redirects the user to it.
  /// </summary>
  [HttpGet("spotify/login")]
  public IActionResult SpotifyLogin()
  {
    try
    {
      var clientId = _configuration["Spotify:ClientId"];
      var callbackUrl = $"{GetBackendUrl()}/api/auth/spotify/callback";
      _logger.LogInformation($"Spotify authorization URL generated: {callbackUrl}");

      var loginRequest = new LoginRequest(new Uri(callbackUrl), clientId!, LoginRequest.ResponseType.Code)
      {
        Scope = new[]
        {
          "user-read-private", "user-read-email", "playlist-read-private",
          "playlist-read-collaborative", "playlist-modify-public", "playlist-modify-private"
        }
      };

      return Redirect(loginRequest.ToUri().ToString());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error generating Spotify authorization URL");
      return Redirect($"{GetFrontendUrl()}/auth?error=server_error");
    }
  }

  /// <summary>
  /// BULLETPROOF Spotify callback - WILL store tokens or show exact error
  /// </summary>
  [HttpGet("spotify/callback")]
  public async Task<IActionResult> SpotifyCallback([FromQuery] string code)
  {
    string step = "start";
    try
    {
      _logger.LogInformation("🚀 SPOTIFY CALLBACK START");

      step = "token_exchange";
      var tokenResponse = await ExchangeCodeForTokensAsync(code);
      _logger.LogInformation("✅ Token exchange successful");

      step = "profile_fetch";
      var config = SpotifyClientConfig.CreateDefault().WithToken(tokenResponse.AccessToken);
      var spotify = new SpotifyClient(config);
      var spotifyProfile = await spotify.UserProfile.Current();
      _logger.LogInformation("✅ Profile fetch successful: {Email}", spotifyProfile.Email);

      step = "user_creation";
      var user = await CreateOrUpdateUserAsync(spotifyProfile);
      _logger.LogInformation("✅ User creation/update successful: UserId={UserId}", user.Id);

      step = "token_storage";
      var scopes = new[] {
        "user-read-private", "user-read-email", "playlist-read-private",
        "playlist-read-collaborative", "playlist-modify-public", "playlist-modify-private"
      };

      var metadata = new
      {
        display_name = spotifyProfile.DisplayName,
        country = spotifyProfile.Country,
        followers = spotifyProfile.Followers?.Total,
        images = spotifyProfile.Images?.Select(i => new { url = i.Url, height = i.Height, width = i.Width })
      };

      await _musicTokenService.StoreTokensAsync(
        user.Id,
        "spotify",
        tokenResponse.AccessToken,
        tokenResponse.RefreshToken,
        tokenResponse.ExpiresIn,
        scopes,
        metadata
      );

      _logger.LogInformation("🔐 Securely stored encrypted tokens for user {UserId}", user.Id);

      _logger.LogInformation("🎉 SPOTIFY CALLBACK COMPLETE - UserId={UserId}", user.Id);
      return Redirect($"{GetFrontendUrl()}/auth/success?user_id={user.Id}");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "💥 SPOTIFY CALLBACK FAILED at step: {Step}", step);
      return Content($"Spotify callback failed at step: {step}. Error: {ex.Message}", "text/plain");
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

      // Always end Supabase session
      // TODO: Implement proper Supabase signout when available
      // await _supabaseAuth.SignOut();

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

  private async Task<AuthorizationCodeTokenResponse> ExchangeCodeForTokensAsync(string code)
  {
    var clientId = _configuration["Spotify:ClientId"];
    var clientSecret = _configuration["Spotify:ClientSecret"];
    var redirectUri = $"{GetBackendUrl()}/api/auth/spotify/callback";

    var request = new AuthorizationCodeTokenRequest(clientId!, clientSecret!, code, new Uri(redirectUri));
    var response = await new OAuthClient().RequestToken(request);

    return response;
  }

  private async Task<UserDto> CreateOrUpdateUserAsync(PrivateUser spotifyProfile)
  {
    try
    {
      // 1. Check if user already exists by email
      var existingUser = await _userService.GetUserByEmailAsync(spotifyProfile.Email);

      if (existingUser != null)
      {
        _logger.LogInformation("Found existing user {UserId} for Spotify email {Email}", existingUser.Id, spotifyProfile.Email);
        return existingUser;
      }

      // 2. Create new user with generated Supabase ID
      var supabaseId = Guid.NewGuid().ToString();
      var newUser = await _userService.CreateUserAsync(
        supabaseId,
        spotifyProfile.DisplayName ?? spotifyProfile.Id,
        spotifyProfile.Email,
        "spotify"
      );

      // 3. Link Spotify provider data
      await _userService.LinkProviderAsync(supabaseId, "spotify", spotifyProfile.Id, new
      {
        display_name = spotifyProfile.DisplayName,
        country = spotifyProfile.Country,
        followers = spotifyProfile.Followers?.Total,
        images = spotifyProfile.Images?.Select(i => new { url = i.Url, height = i.Height, width = i.Width })
      });

      _logger.LogInformation("Created new user {UserId} for Spotify profile {SpotifyId}", newUser.Id, spotifyProfile.Id);
      return newUser;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error creating/updating user for Spotify profile {SpotifyId}", spotifyProfile.Id);
      throw;
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
