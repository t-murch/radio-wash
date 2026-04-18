using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace RadioWash.Api.Infrastructure.Authentication;

/// <summary>
/// Environment-gated policy for Supabase JWT validation. Encapsulates two rules that must
/// hold together to prevent token forgery in production:
/// 1. The HS256 symmetric signing key (derived from <c>Supabase:JwtSecret</c>) is registered
///    only in Development/Testing. Production Supabase Cloud uses ES256/RS256 via JWKS; a
///    symmetric key accepted in production would let anyone with the JWT secret forge tokens.
/// 2. <c>ValidAlgorithms</c> is pinned to RS256/ES256 in Production. When null, the validator
///    accepts every algorithm it knows, which opens the door to algorithm-confusion attacks.
/// </summary>
public static class SupabaseJwtPolicy
{
    private static readonly string[] ProductionAlgorithms =
    {
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.EcdsaSha256,
    };

    public static bool AllowHs256(IHostEnvironment environment) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Testing")
        || environment.IsEnvironment("Test");

    public static IEnumerable<string>? ValidAlgorithmsFor(IHostEnvironment environment) =>
        environment.IsProduction() ? ProductionAlgorithms : null;
}
