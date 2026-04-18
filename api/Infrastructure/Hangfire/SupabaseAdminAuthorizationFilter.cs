using global::Hangfire.Dashboard;

namespace RadioWash.Api.Infrastructure.Hangfire;

/// <summary>
/// Hangfire dashboard authorization filter. Delegates to <see cref="SupabaseAdminAuthorization"/>
/// for the actual decision — this adapter's only job is unwrapping the HttpContext and
/// extracting the authenticated principal.
/// </summary>
public class SupabaseAdminAuthorizationFilter : IDashboardAsyncAuthorizationFilter
{
  private readonly SupabaseAdminAuthorization _authorization;

  public SupabaseAdminAuthorizationFilter(SupabaseAdminAuthorization authorization)
  {
    _authorization = authorization;
  }

  public Task<bool> AuthorizeAsync(DashboardContext context)
  {
    var httpContext = context.GetHttpContext();
    return Task.FromResult(_authorization.IsAllowed(httpContext.User));
  }
}
