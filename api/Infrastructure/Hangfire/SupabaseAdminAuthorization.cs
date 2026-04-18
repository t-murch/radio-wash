using System.Security.Claims;

namespace RadioWash.Api.Infrastructure.Hangfire;

/// <summary>
/// Pure-predicate core of the Hangfire dashboard authorization check. Decides whether a given
/// <see cref="ClaimsPrincipal"/> is an admin by comparing its NameIdentifier claim (the Supabase
/// user ID) against a configured allowlist. Isolated from Hangfire's DashboardContext so the
/// decision logic is testable without instantiating framework types.
/// </summary>
public class SupabaseAdminAuthorization
{
  private readonly HashSet<string> _adminSupabaseIds;

  public SupabaseAdminAuthorization(IReadOnlyList<string> adminSupabaseIds)
  {
    _adminSupabaseIds = new HashSet<string>(adminSupabaseIds, StringComparer.Ordinal);
  }

  public bool IsAllowed(ClaimsPrincipal? user)
  {
    if (user?.Identity is not { IsAuthenticated: true })
    {
      return false;
    }

    var supabaseId = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(supabaseId))
    {
      return false;
    }

    return _adminSupabaseIds.Contains(supabaseId);
  }
}
