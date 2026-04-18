using System.Security.Claims;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Middleware;

/// <summary>
/// Middleware that proactively refreshes expiring music service tokens
/// Runs early in the request pipeline to ensure valid tokens for downstream services
/// </summary>
public class TokenRefreshMiddleware
{
  private static readonly IReadOnlyList<string> DefaultPathPrefixes = new[]
  {
    "/api/playlist",
    "/api/jobs",
    "/api/spotify",
    "/api/cleanplaylist",
  };

  private readonly RequestDelegate _next;
  private readonly ILogger<TokenRefreshMiddleware> _logger;
  private readonly IReadOnlyList<string> _pathPrefixes;

  public TokenRefreshMiddleware(
    RequestDelegate next,
    ILogger<TokenRefreshMiddleware> logger,
    IReadOnlyList<string>? pathPrefixes = null)
  {
    _next = next;
    _logger = logger;
    _pathPrefixes = pathPrefixes ?? DefaultPathPrefixes;
  }

  public async Task InvokeAsync(HttpContext context, IMusicTokenService musicTokenService, IUserService userService, IEnumerable<IMusicTokenRefresher> refreshers)
  {
    // Only process authenticated requests that might need music service tokens
    if (context.User.Identity?.IsAuthenticated == true && ShouldCheckTokens(context))
    {
      try
      {
        await RefreshTokensIfNeededAsync(context, musicTokenService, userService, refreshers);
      }
      catch (Exception ex)
      {
        // Log but don't fail the request - token refresh is best-effort
        _logger.LogWarning(ex, "Failed to refresh tokens in middleware");
      }
    }

    await _next(context);
  }

  private bool ShouldCheckTokens(HttpContext context)
  {
    var path = context.Request.Path.Value?.ToLower() ?? "";
    return _pathPrefixes.Any(prefix => path.StartsWith(prefix));
  }

  private async Task RefreshTokensIfNeededAsync(
    HttpContext context,
    IMusicTokenService musicTokenService,
    IUserService userService,
    IEnumerable<IMusicTokenRefresher> refreshers)
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

    // Loop over every provider with a registered refresher strategy. Adding Apple Music is a
    // single IMusicTokenRefresher registration in Program.cs — the middleware picks it up
    // without code changes here.
    foreach (var refresher in refreshers)
    {
      var tokenInfo = await musicTokenService.GetTokenInfoAsync(user.Id, refresher.ProviderName);
      if (tokenInfo != null && tokenInfo.IsExpired && tokenInfo.CanRefresh)
      {
        _logger.LogInformation(
          "Proactively refreshing {Provider} tokens for user {UserId}",
          refresher.ProviderName, user.Id);
        await musicTokenService.RefreshTokensAsync(user.Id, refresher.ProviderName);
      }
    }
  }
}
