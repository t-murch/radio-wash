// api/Controllers/AuthController.cs
using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
  private readonly IAuthService _authService;
  private readonly ILogger<AuthController> _logger;

  public AuthController(
      IAuthService authService,
      ILogger<AuthController> logger)
  {
    _authService = authService;
    _logger = logger;
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

      HttpContext.Response.Headers.Append("Access-Control-Allow-Credentials", "true");
      // Store state in session or cookie to validate later
      HttpContext.Response.Cookies.Append("spotify_auth_state", state, new CookieOptions
      {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        MaxAge = TimeSpan.FromMinutes(10)
      });

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
      // Verify state parameter to prevent CSRF attacks
      if (!HttpContext.Request.Cookies.TryGetValue("spotify_auth_state", out var storedState) || state != storedState)
      {
        // Log the state mismatch
        _logger.LogWarning("State mismatch: expected {ExpectedState}, received {ReceivedState}", storedState, state);
        // Log all cookeis
        foreach (var cookie in HttpContext.Request.Cookies)
        {
          _logger.LogInformation("Cookie: {CookieName}={CookieValue}", cookie.Key, cookie.Value);
        }
        return BadRequest(new { error = "State validation failed" });
      }

      // Clear the state cookie
      HttpContext.Response.Cookies.Delete("spotify_auth_state");

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
