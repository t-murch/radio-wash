using Microsoft.Extensions.Configuration;

namespace RadioWash.Api.Infrastructure.Authentication;

/// <summary>
/// Startup guard for local development runs.
/// </summary>
/// <remarks>
/// <para>
/// <c>api/.dockerignore</c> excludes <c>appsettings.*.json</c> so developer secrets are never
/// baked into an image. The consequence is that a containerised run has no
/// <c>appsettings.Development.json</c>: the only config present is <c>appsettings.json</c>,
/// which carries the deployed Supabase project's URL and JWT secret.
/// </para>
/// <para>
/// Nothing about that combination looks broken at boot. The API starts, migrations run, and the
/// failure only appears later as unexplained 401s (tokens validated against the wrong issuer)
/// and CORS-blocked browser requests. The underlying cause — configuration, not code — is
/// invisible in the logs, so this guard converts it into a startup failure that names the
/// missing variables.
/// </para>
/// <para>
/// Development only. Production supplies these through its own environment and must never
/// take a hard dependency on a local <c>.env</c>.
/// </para>
/// </remarks>
public static class LocalDevelopmentConfigurationGuard
{
    /// <summary>
    /// Hosts that indicate configuration is pointing at a deployed environment. A local run
    /// reaching these means <c>appsettings.json</c> leaked through in place of local values.
    /// </summary>
    private static readonly string[] NonLocalHostMarkers =
    {
        ".supabase.co",
        "azurecontainerapps.io",
    };

    public static void Validate(IConfiguration configuration, string frontendUrl)
    {
        var problems = new List<string>();

        var supabaseUrl = configuration["Supabase:PublicUrl"];
        if (string.IsNullOrWhiteSpace(supabaseUrl))
        {
            problems.Add("Supabase:PublicUrl is not set. Expected SUPABASE_API_PORT in .env.");
        }
        else if (PointsAtDeployedEnvironment(supabaseUrl))
        {
            problems.Add(
                $"Supabase:PublicUrl is '{supabaseUrl}', a deployed project. Local tokens are " +
                "signed by the local GoTrue and will fail validation against it. Set " +
                "SUPABASE_API_PORT in .env.");
        }

        if (string.IsNullOrWhiteSpace(configuration["Supabase:JwtSecret"]))
        {
            problems.Add("Supabase:JwtSecret is not set. Copy it from `supabase status` into SUPABASE_JWT_SECRET in .env.");
        }

        if (PointsAtDeployedEnvironment(frontendUrl))
        {
            problems.Add(
                $"FrontendUrl is '{frontendUrl}', a deployed origin. CORS admits exactly one " +
                "origin, so the local browser will be blocked. Set FRONTEND_URL in .env.");
        }

        if (problems.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Local development configuration is incomplete. The API is running in Development but " +
            "is configured for a deployed environment, which would surface as unexplained 401s and " +
            "CORS failures rather than a clear error." + Environment.NewLine + Environment.NewLine +
            string.Join(Environment.NewLine, problems.Select(p => "  - " + p)) +
            Environment.NewLine + Environment.NewLine +
            "Copy .env.example to .env and fill it in from `supabase status`. See README " +
            "\"Local development\" for the full walkthrough.");
    }

    private static bool PointsAtDeployedEnvironment(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && NonLocalHostMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
