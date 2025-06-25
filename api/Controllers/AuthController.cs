using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Models.Requests;

namespace RadioWash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        IAuthService authService,
        ILogger<AuthController> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _authService = authService;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    /// <summary>
    /// Register a new user with Supabase
    /// </summary>
    [HttpPost("signup")]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
    {
        try
        {
            var result = await _authService.SignUpAsync(request.Email, request.Password, request.DisplayName);
            
            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage });
            }

            SetAuthCookie(result.Token!);
            return Ok(new { user = result.User, message = "User registered successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user registration");
            return StatusCode(500, new { error = "Registration failed" });
        }
    }

    /// <summary>
    /// Sign in with email and password
    /// </summary>
    [HttpPost("signin")]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest request)
    {
        try
        {
            var result = await _authService.SignInAsync(request.Email, request.Password);
            
            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage });
            }

            SetAuthCookie(result.Token!);
            return Ok(new { user = result.User, message = "Signed in successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sign in");
            return StatusCode(500, new { error = "Sign in failed" });
        }
    }

    /// <summary>
    /// Sign out the current user
    /// </summary>
    [HttpPost("signout")]
    [Authorize]
    public async Task<IActionResult> SignOut()
    {
        try
        {
            await _authService.SignOutAsync();
            ClearAuthCookie();
            return Ok(new { message = "Signed out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sign out");
            return StatusCode(500, new { error = "Sign out failed" });
        }
    }

    /// <summary>
    /// Get the current authenticated user
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        try
        {
            var supabaseUserId = GetSupabaseUserId();
            if (supabaseUserId == null)
            {
                return Unauthorized(new { error = "Invalid user token" });
            }

            var user = await _authService.GetUserBySupabaseIdAsync(supabaseUserId.Value);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user");
            return StatusCode(500, new { error = "Failed to get user profile" });
        }
    }

    /// <summary>
    /// Refresh the current user's authentication token
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken()
    {
        try
        {
            var refreshToken = Request.Cookies["rw-refresh-token"];
            if (string.IsNullOrEmpty(refreshToken))
            {
                return Unauthorized(new { error = "No refresh token provided" });
            }

            var result = await _authService.RefreshTokenAsync(refreshToken);
            if (!result.Success)
            {
                ClearAuthCookie();
                return Unauthorized(new { error = result.ErrorMessage });
            }

            SetAuthCookie(result.Token!);
            return Ok(new { message = "Token refreshed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return StatusCode(500, new { error = "Token refresh failed" });
        }
    }

    private void SetAuthCookie(string token)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTime.UtcNow.AddDays(7)
        };

        Response.Cookies.Append("rw-auth-token", token, cookieOptions);
    }

    private void ClearAuthCookie()
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
        Response.Cookies.Delete("rw-refresh-token", cookieOptions);
    }

    private Guid? GetSupabaseUserId()
    {
        var subClaim = User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(subClaim) || !Guid.TryParse(subClaim, out var userId))
        {
            return null;
        }
        return userId;
    }
}