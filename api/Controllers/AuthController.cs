// api/Controllers/AuthController.cs
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

  public AuthController(
      IAuthService authService,
      ILogger<AuthController> logger,
      IMemoryCache memoryCache)
  {
    _authService = authService;
    _logger = logger;
    _memoryCache = memoryCache;
  }

  /// <summary>
  /// Generates a Spotify authorization URL
  /// </summary>
  [HttpGet("login")]
  public IActionResult Login()
  {
    try
    {
      // Generate a random state to prevent CSRF attacks
      var state = Guid.NewGuid().ToString();

      // Store state in memory cache with 10 minute expiration
      _memoryCache.Set($"auth_state_{state}", true, TimeSpan.FromMinutes(10));

      // Generate authorization URL
      var authUrl = _authService.GenerateAuthUrl(state);

      return Ok(new { url = authUrl });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error generating Spotify authorization URL");
      return StatusCode(500, new { error = "Failed to generate authorization URL" });
    }
  }

  /// <summary>
  /// Handles the callback from Spotify authorization
  /// </summary>
  [HttpGet("callback")]
  public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
  {
    try
    {

      // Retrieve state from memory cache
      if (!_memoryCache.TryGetValue($"auth_state_{state}", out _))
      {
        _logger.LogWarning("State not found in cache: {State}", state);
        return BadRequest(new { error = "Invalid or expired state" });
      }

      // Remove state from cache after validation
      _memoryCache.Remove($"auth_state_{state}");

      // Exchange authorization code for access token
      var authResponse = await _authService.HandleCallbackAsync(code);

      return Ok(authResponse);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling Spotify callback");
      return StatusCode(500, new { error = "Failed to authenticate with Spotify" });
    }
  }

  /// <summary>
  /// Validates the user's token and refreshes it if necessary
  /// </summary>
  [HttpGet("validate")]
  public async Task<IActionResult> ValidateToken([FromQuery] int userId)
  {
    try
    {
      var isValid = await _authService.ValidateTokenAsync(userId);

      if (!isValid)
      {
        return Unauthorized(new { error = "Token is invalid or expired" });
      }

      return Ok(new { valid = true });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error validating token for user {UserId}", userId);
      return StatusCode(500, new { error = "Failed to validate token" });
    }
  }
}
