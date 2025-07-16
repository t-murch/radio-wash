using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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

  public AuthController(
      ILogger<AuthController> logger,
      IMemoryCache memoryCache,
      IConfiguration configuration,
      IWebHostEnvironment environment,
      Supabase.Gotrue.Client supabaseAuth,
      IUserService userService)
  {
    _logger = logger;
    _memoryCache = memoryCache;
    _configuration = configuration;
    _environment = environment;
    _supabaseAuth = supabaseAuth;
    _userService = userService;
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
  /// Handles the callback from Spotify, creates/updates Supabase user, and redirects to frontend.
  /// </summary>
  [HttpGet("spotify/callback")]
  public async Task<IActionResult> SpotifyCallback([FromQuery] string code)
  {
    try
    {
      // 1. Exchange code for tokens with Spotify
      var tokenResponse = await ExchangeCodeForTokensAsync(code);

      // 2. Get user profile from Spotify
      var config = SpotifyClientConfig.CreateDefault().WithToken(tokenResponse.AccessToken);
      var spotify = new SpotifyClient(config);
      var spotifyProfile = await spotify.UserProfile.Current();

      // 3. Find or create Supabase Auth user
      var supabaseUser = await GetOrCreateSupabaseUserAsync(spotifyProfile.Email);

      // 4. Create or update user in our database
      await CreateOrUpdateUserAsync(supabaseUser, spotifyProfile, tokenResponse);

      // 5. For now, skip the Supabase session creation
      _logger.LogInformation("Successfully processed Spotify auth for {Email}", spotifyProfile.Email);

      // 6. Redirect to frontend with success
      var redirectUrl = $"{GetFrontendUrl()}/auth/callback?email={Uri.EscapeDataString(spotifyProfile.Email)}";
      return Redirect(redirectUrl);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling Spotify callback");
      return Redirect($"{GetFrontendUrl()}/auth?error=authentication_failed");
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
  /// Logs the user out by calling Supabase signout.
  /// </summary>
  [HttpPost("logout")]
  [Authorize]
  public Task<IActionResult> Logout()
  {
    try
    {
      // TODO: Implement proper Supabase signout
      _logger.LogInformation("User logged out");
      return Task.FromResult<IActionResult>(Ok(new { success = true }));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error during logout");
      return Task.FromResult<IActionResult>(StatusCode(500, new { error = "Failed to logout" }));
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

  private Task<string> GetOrCreateSupabaseUserAsync(string email)
  {
    // For now, just return a placeholder - we'll improve this later
    // In a real implementation, you'd use Supabase admin functions to create users
    _logger.LogInformation("TODO: Implement proper Supabase user creation for {Email}", email);
    return Task.FromResult(Guid.NewGuid().ToString());
  }

  private Task CreateOrUpdateUserAsync(string supabaseUserId, PrivateUser spotifyProfile, AuthorizationCodeTokenResponse tokens)
  {
    // TODO: Implement user service to create/update user with encrypted tokens
    // This will use the EncryptionService to encrypt the Spotify tokens before storing
    _logger.LogInformation("TODO: Create/update user {SpotifyId} with Supabase ID {SupabaseId}",
      spotifyProfile.Id, supabaseUserId);
    return Task.CompletedTask;
  }

  private string GetFrontendUrl()
  {
    return _configuration["FrontendUrl"] ?? "http://localhost:3000";
  }

  private string GetBackendUrl()
  {
    return _configuration["BackendUrl"] ?? "https://localhost:7165";
  }
}
