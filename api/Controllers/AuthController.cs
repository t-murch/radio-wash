using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
  private readonly IAuthService _authService;
  private readonly ILogger<AuthController> _logger;
  private readonly IMemoryCache _memoryCache;
  private readonly IConfiguration _configuration;
  private readonly IWebHostEnvironment _environment;

  public AuthController(
      IAuthService authService,
      ILogger<AuthController> logger,
      IMemoryCache memoryCache,
      IConfiguration configuration,
      IWebHostEnvironment environment)
  {
    _authService = authService;
    _logger = logger;
    _memoryCache = memoryCache;
    _configuration = configuration;
    _environment = environment;
  }

  /// <summary>
  /// Generates a Spotify authorization URL and redirects the user to it.
  /// </summary>
  [HttpGet("login")]
  public IActionResult Login()
  {
    try
    {
      var state = Guid.NewGuid().ToString();
      _memoryCache.Set($"auth_state_{state}", true, TimeSpan.FromMinutes(10));
      var authUrl = _authService.GenerateAuthUrl(state);
      return Redirect(authUrl);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error generating Spotify authorization URL");
      return Redirect($"{GetFrontendUrl()}/auth?error=server_error");
    }
  }

  /// <summary>
  /// Handles the callback from Spotify, sets a cookie, and redirects to the frontend dashboard.
  /// </summary>
  [HttpGet("callback")]
  public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
  {
    try
    {
      if (!_memoryCache.TryGetValue($"auth_state_{state}", out _))
      {
        _logger.LogWarning("State not found in cache: {State}", state);
        return Redirect($"{GetFrontendUrl()}/auth?error=invalid_state");
      }
      _memoryCache.Remove($"auth_state_{state}");

      var authResponse = await _authService.HandleCallbackAsync(code);

      var cookieOptions = new CookieOptions
      {
        HttpOnly = true,
        // Correctly check the environment
        Secure = !_environment.IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = DateTime.UtcNow.AddDays(7)
      };

      Response.Cookies.Append("rw-auth-token", authResponse.Token, cookieOptions);

      return Redirect($"{GetFrontendUrl()}/auth/callback");
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
      if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
      {
        return Unauthorized(new { error = "User ID not found in token." });
      }

      var user = await _authService.GetUserByIdAsync(userId);
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
  /// Logs the user out by clearing the authentication cookie.
  /// </summary>
  [HttpPost("logout")]
  public IActionResult Logout()
  {
    var cookieOptions = new CookieOptions
    {
      HttpOnly = true,
      Secure = !_environment.IsDevelopment(),
      SameSite = SameSiteMode.Lax,
      Path = "/",
      Expires = DateTime.UtcNow.AddDays(-1)
    };
    Response.Cookies.Delete("rw-auth-token", cookieOptions);
    return Ok(new { success = true });
  }

  private string GetFrontendUrl()
  {
    return _configuration["FrontendUrl"] ?? "https://127.0.0.1:3000";
  }
}
