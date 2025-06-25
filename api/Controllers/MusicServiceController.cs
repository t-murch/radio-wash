using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MusicServiceController : ControllerBase
{
    private readonly IMusicServiceAuthService _musicServiceAuthService;
    private readonly ILogger<MusicServiceController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public MusicServiceController(
        IMusicServiceAuthService musicServiceAuthService,
        ILogger<MusicServiceController> logger,
        IMemoryCache memoryCache,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _musicServiceAuthService = musicServiceAuthService;
        _logger = logger;
        _memoryCache = memoryCache;
        _configuration = configuration;
        _environment = environment;
    }

    /// <summary>
    /// Get connected music services for the current user
    /// </summary>
    [HttpGet("connected")]
    public async Task<IActionResult> GetConnectedServices()
    {
        try
        {
            var supabaseUserId = GetSupabaseUserId();
            if (supabaseUserId == null)
            {
                return Unauthorized(new { error = "Invalid user token" });
            }

            var services = await _musicServiceAuthService.GetConnectedServicesAsync(supabaseUserId.Value);
            return Ok(services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting connected services");
            return StatusCode(500, new { error = "Failed to get connected services" });
        }
    }

    /// <summary>
    /// Initiate Spotify OAuth flow
    /// </summary>
    [HttpGet("spotify/auth")]
    public IActionResult SpotifyAuth()
    {
        try
        {
            var supabaseUserId = GetSupabaseUserId();
            if (supabaseUserId == null)
            {
                return Unauthorized(new { error = "Invalid user token" });
            }

            var state = Guid.NewGuid().ToString();
            _memoryCache.Set($"spotify_auth_state_{state}", supabaseUserId.Value, TimeSpan.FromMinutes(10));
            
            var authUrl = _musicServiceAuthService.GenerateSpotifyAuthUrl(state);
            return Redirect(authUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Spotify authorization URL");
            return Redirect($"{GetFrontendUrl()}/settings?error=spotify_auth_failed");
        }
    }

    /// <summary>
    /// Handle Spotify OAuth callback
    /// </summary>
    [HttpGet("spotify/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> SpotifyCallback([FromQuery] string code, [FromQuery] string state)
    {
        try
        {
            if (!_memoryCache.TryGetValue($"spotify_auth_state_{state}", out Guid userId))
            {
                _logger.LogWarning("Spotify auth state not found: {State}", state);
                return Redirect($"{GetFrontendUrl()}/settings?error=invalid_state");
            }
            _memoryCache.Remove($"spotify_auth_state_{state}");

            await _musicServiceAuthService.HandleSpotifyCallbackAsync(userId, code);
            return Redirect($"{GetFrontendUrl()}/settings?spotify_connected=true");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Spotify callback");
            return Redirect($"{GetFrontendUrl()}/settings?error=spotify_connection_failed");
        }
    }

    /// <summary>
    /// Initiate Apple Music OAuth flow
    /// </summary>
    [HttpGet("apple/auth")]
    public IActionResult AppleMusicAuth()
    {
        try
        {
            var supabaseUserId = GetSupabaseUserId();
            if (supabaseUserId == null)
            {
                return Unauthorized(new { error = "Invalid user token" });
            }

            var state = Guid.NewGuid().ToString();
            _memoryCache.Set($"apple_auth_state_{state}", supabaseUserId.Value, TimeSpan.FromMinutes(10));
            
            var authUrl = _musicServiceAuthService.GenerateAppleMusicAuthUrl(state);
            return Redirect(authUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Apple Music authorization URL");
            return Redirect($"{GetFrontendUrl()}/settings?error=apple_auth_failed");
        }
    }

    /// <summary>
    /// Handle Apple Music OAuth callback
    /// </summary>
    [HttpGet("apple/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> AppleMusicCallback([FromQuery] string code, [FromQuery] string state)
    {
        try
        {
            if (!_memoryCache.TryGetValue($"apple_auth_state_{state}", out Guid userId))
            {
                _logger.LogWarning("Apple Music auth state not found: {State}", state);
                return Redirect($"{GetFrontendUrl()}/settings?error=invalid_state");
            }
            _memoryCache.Remove($"apple_auth_state_{state}");

            await _musicServiceAuthService.HandleAppleMusicCallbackAsync(userId, code);
            return Redirect($"{GetFrontendUrl()}/settings?apple_connected=true");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Apple Music callback");
            return Redirect($"{GetFrontendUrl()}/settings?error=apple_connection_failed");
        }
    }

    /// <summary>
    /// Disconnect a music service
    /// </summary>
    [HttpDelete("{service}")]
    public async Task<IActionResult> DisconnectService(string service)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserId();
            if (supabaseUserId == null)
            {
                return Unauthorized(new { error = "Invalid user token" });
            }

            if (!Enum.TryParse<MusicServiceType>(service, true, out var serviceType))
            {
                return BadRequest(new { error = "Invalid service type" });
            }

            await _musicServiceAuthService.DisconnectServiceAsync(supabaseUserId.Value, serviceType);
            return Ok(new { message = $"{service} disconnected successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting service {Service}", service);
            return StatusCode(500, new { error = "Failed to disconnect service" });
        }
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

    private string GetFrontendUrl()
    {
        return _configuration["FrontendUrl"] ?? "http://localhost:3000";
    }
}

public enum MusicServiceType
{
    Spotify,
    AppleMusic
}