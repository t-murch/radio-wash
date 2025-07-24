using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Infrastructure.Data;
using System.Security.Claims;

namespace RadioWash.Api.Controllers;

[Authorize]
[ApiController]
public abstract class AuthenticatedControllerBase : ControllerBase
{
  private readonly RadioWashDbContext _dbContext;
  protected readonly ILogger Logger;

  protected AuthenticatedControllerBase(RadioWashDbContext dbContext, ILogger logger)
  {
    _dbContext = dbContext;
    Logger = logger;
  }

  protected int GetCurrentUserId()
  {
    var supabaseId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    Logger.LogInformation("Retrieved SupabaseId from claims: {SupabaseId}", supabaseId);
    if (string.IsNullOrEmpty(supabaseId))
    {
      Logger.LogWarning("Authentication failed: No NameIdentifier claim found");
      throw new UnauthorizedAccessException("User not authenticated");
    }

    var user = _dbContext.Users.FirstOrDefault(u => u.SupabaseId == supabaseId);
    if (user == null)
    {
      Logger.LogWarning("User lookup failed: No user found for SupabaseId {SupabaseId}", supabaseId);
      throw new UnauthorizedAccessException("User not found");
    }

    Logger.LogDebug("User authenticated: SupabaseId {SupabaseId} mapped to UserId {UserId}", supabaseId, user.Id);
    return user.Id;
  }

  protected string GetCurrentSupabaseUserId()
  {
    var supabaseId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(supabaseId))
    {
      Logger.LogWarning("Authentication failed: No NameIdentifier claim found");
      throw new UnauthorizedAccessException("User not authenticated");
    }

    Logger.LogDebug("Retrieved SupabaseId from claims: {SupabaseId}", supabaseId);
    return supabaseId;
  }
}
