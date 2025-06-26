using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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

  public AuthController(
      ILogger<AuthController> logger,
      IMemoryCache memoryCache,
      IConfiguration configuration,
      IWebHostEnvironment environment)
  {
    _logger = logger;
    _memoryCache = memoryCache;
    _configuration = configuration;
    _environment = environment;

    // Initialize Supabase Auth client
    var supabaseUrl = _configuration["Supabase:Url"];
    var supabaseKey = _configuration["Supabase:ServiceRoleKey"];
    _supabaseAuth = new Supabase.Gotrue.Client(new ClientOptions
    {
      Url = $"{supabaseUrl}/auth/v1",
      Headers = new Dictionary<string, string>
      {
        ["apikey"] = supabaseKey!,
        ["Authorization"] = $"Bearer {supabaseKey}"
      }
    });
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
      var spotify = new SpotifyAPI(tokenResponse.AccessToken);
      var spotifyProfile = await spotify.UserProfile.Current();

      // 3. Find or create Supabase Auth user
      var supabaseUser = await GetOrCreateSupabaseUserAsync(spotifyProfile.Email);
      
      // 4. Create or update user in our database
      await CreateOrUpdateUserAsync(supabaseUser.Id, spotifyProfile, tokenResponse);

      // 5. Generate session for the user
      var session = await _supabaseAuth.SignInWithPassword(spotifyProfile.Email, "temp-password");

      // 6. Redirect to frontend with session tokens
      var redirectUrl = $"{GetFrontendUrl()}/auth/callback#access_token={session.AccessToken}&refresh_token={session.RefreshToken}";
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
    try
    {
      var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
      if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
      {
        return Unauthorized(new { error = "User ID not found in token." });
      }

      // TODO: Implement user service to get user by Supabase ID
      // var user = await _userService.GetUserBySupabaseIdAsync(userId);
      
      return Ok(new { message = "User profile endpoint - TODO: implement user service" });
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
  public async Task<IActionResult> Logout()
  {
    try
    {
      await _supabaseAuth.SignOut();
      return Ok(new { success = true });
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

  private async Task<Supabase.Gotrue.User> GetOrCreateSupabaseUserAsync(string email)
  {
    try
    {
      // Try to get existing user
      var existingUser = await _supabaseAuth.Admin.GetUserByEmail(email);
      if (existingUser != null)
      {
        return existingUser;
      }
    }
    catch
    {
      // User doesn't exist, continue to create
    }

    // Create new user
    var newUser = await _supabaseAuth.Admin.CreateUser(new AdminCreateUserRequest
    {
      Email = email,
      Password = Guid.NewGuid().ToString(), // Generate random password
      EmailConfirm = true
    });

    return newUser;
  }

  private async Task CreateOrUpdateUserAsync(string supabaseUserId, PrivateUser spotifyProfile, AuthorizationCodeTokenResponse tokens)
  {
    // TODO: Implement user service to create/update user with encrypted tokens
    // This will use the EncryptionService to encrypt the Spotify tokens before storing
    _logger.LogInformation("TODO: Create/update user {SpotifyId} with Supabase ID {SupabaseId}", 
      spotifyProfile.Id, supabaseUserId);
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