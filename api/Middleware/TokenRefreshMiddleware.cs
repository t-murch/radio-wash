using System.Security.Claims;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Middleware;

/// <summary>
/// Middleware that proactively refreshes expiring music service tokens
/// Runs early in the request pipeline to ensure valid tokens for downstream services
/// </summary>
public class TokenRefreshMiddleware
{
  private readonly RequestDelegate _next;
  private readonly ILogger<TokenRefreshMiddleware> _logger;

  public TokenRefreshMiddleware(RequestDelegate next, ILogger<TokenRefreshMiddleware> logger)
  {
    _next = next;
    _logger = logger;
  }

  public async Task InvokeAsync(HttpContext context, IMusicTokenService musicTokenService, IUserService userService)
  {
    // Only process authenticated requests that might need music service tokens
    if (context.User.Identity?.IsAuthenticated == true && ShouldCheckTokens(context))
    {
      try
      {
        await RefreshTokensIfNeededAsync(context, musicTokenService, userService);
      }
      catch (Exception ex)
      {
        // Log but don't fail the request - token refresh is best-effort
        _logger.LogWarning(ex, "Failed to refresh tokens in middleware");
      }
    }

    await _next(context);
  }

  private static bool ShouldCheckTokens(HttpContext context)
  {
    var path = context.Request.Path.Value?.ToLower() ?? "";

    // Only check tokens for API endpoints that use music services
    return path.StartsWith("/api/playlist") ||
           path.StartsWith("/api/jobs") ||
           path.StartsWith("/api/spotify");
  }

  private async Task RefreshTokensIfNeededAsync(HttpContext context, IMusicTokenService musicTokenService, IUserService userService)
  {
    var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var supabaseId))
    {
      return;
    }

    var user = await userService.GetUserBySupabaseIdAsync(supabaseId);
    if (user == null)
    {
      return;
    }

    // Check Spotify tokens
    var spotifyTokenInfo = await musicTokenService.GetTokenInfoAsync(user.Id, "spotify");
    if (spotifyTokenInfo != null && spotifyTokenInfo.IsExpired && spotifyTokenInfo.CanRefresh)
    {
      _logger.LogInformation("Proactively refreshing Spotify tokens for user {UserId}", user.Id);
      await musicTokenService.RefreshTokensAsync(user.Id, "spotify");
    }

    // Add other providers here as needed
    // e.g., Apple Music, YouTube Music, etc.
  }
}
