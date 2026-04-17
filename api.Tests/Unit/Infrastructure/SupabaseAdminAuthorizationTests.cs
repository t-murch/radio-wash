using System.Security.Claims;
using RadioWash.Api.Infrastructure.Hangfire;

namespace RadioWash.Api.Tests.Unit.Infrastructure;

/// <summary>
/// Tests for <see cref="SupabaseAdminAuthorization"/> — the pure-predicate core of the
/// Hangfire dashboard authorization filter. The filter itself is a thin adapter that unwraps
/// the <c>HttpContext</c> from the Hangfire <c>DashboardContext</c>, so we test the decision
/// logic in isolation without constructing Hangfire framework types.
/// </summary>
public class SupabaseAdminAuthorizationTests
{
  private const string AdminSupabaseId = "admin-user-abc";
  private const string NonAdminSupabaseId = "plain-user-xyz";

  private static readonly IReadOnlyList<string> Allowlist = new[] { AdminSupabaseId };

  [Fact]
  public void IsAllowed_WithNoPrincipal_ReturnsFalse()
  {
    var auth = new SupabaseAdminAuthorization(Allowlist);

    Assert.False(auth.IsAllowed(user: null));
  }

  [Fact]
  public void IsAllowed_WithUnauthenticatedPrincipal_ReturnsFalse()
  {
    var auth = new SupabaseAdminAuthorization(Allowlist);
    var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // no authentication type

    Assert.False(auth.IsAllowed(anonymous));
  }

  [Fact]
  public void IsAllowed_WithAuthenticatedNonAdmin_ReturnsFalse()
  {
    var auth = new SupabaseAdminAuthorization(Allowlist);
    var principal = MakeAuthenticatedPrincipal(NonAdminSupabaseId);

    Assert.False(auth.IsAllowed(principal));
  }

  [Fact]
  public void IsAllowed_WithAuthenticatedAdminInAllowlist_ReturnsTrue()
  {
    var auth = new SupabaseAdminAuthorization(Allowlist);
    var principal = MakeAuthenticatedPrincipal(AdminSupabaseId);

    Assert.True(auth.IsAllowed(principal));
  }

  [Fact]
  public void IsAllowed_WithMissingNameIdentifierClaim_ReturnsFalse()
  {
    var auth = new SupabaseAdminAuthorization(Allowlist);
    // Authenticated but no NameIdentifier claim at all.
    var identity = new ClaimsIdentity(new[] { new Claim("email", "someone@example.com") }, authenticationType: "Bearer");
    var principal = new ClaimsPrincipal(identity);

    Assert.False(auth.IsAllowed(principal));
  }

  [Fact]
  public void IsAllowed_WithEmptyAllowlist_RejectsEveryone()
  {
    // Empty allowlist means Hangfire dashboard is effectively closed to all users. Safer than
    // open-by-default; production must explicitly configure admins.
    var auth = new SupabaseAdminAuthorization(Array.Empty<string>());
    var principal = MakeAuthenticatedPrincipal(AdminSupabaseId);

    Assert.False(auth.IsAllowed(principal));
  }

  [Theory]
  [InlineData("ADMIN-USER-ABC")] // different case
  [InlineData(" admin-user-abc ")] // whitespace
  public void IsAllowed_AllowlistComparisonIsCaseSensitiveAndExact(string claimValue)
  {
    // Supabase user IDs are UUIDs emitted lowercase; do not match on case-insensitive or
    // trimmed comparisons — these are security boundaries, not user-friendly inputs.
    var auth = new SupabaseAdminAuthorization(Allowlist);
    var principal = MakeAuthenticatedPrincipal(claimValue);

    Assert.False(auth.IsAllowed(principal));
  }

  private static ClaimsPrincipal MakeAuthenticatedPrincipal(string supabaseId)
  {
    var identity = new ClaimsIdentity(
      new[] { new Claim(ClaimTypes.NameIdentifier, supabaseId) },
      authenticationType: "Bearer");
    return new ClaimsPrincipal(identity);
  }
}
