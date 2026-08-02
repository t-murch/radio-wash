using Microsoft.Extensions.Configuration;
using RadioWash.Api.Infrastructure.Authentication;

namespace RadioWash.Api.Tests.Unit.Infrastructure;

/// <summary>
/// Covers the startup guard for local development configuration. The failure being guarded:
/// `api/.dockerignore` excludes `appsettings.*.json`, so a containerised Development run sees
/// only `appsettings.json` and silently configures itself against the deployed Supabase
/// project. That surfaces as blanket 401s and CORS-blocked requests with nothing in the logs
/// naming configuration as the cause, so the guard must turn it into a startup failure.
/// </summary>
public class LocalDevelopmentConfigurationGuardTests
{
    private const string LocalSupabaseUrl = "http://127.0.0.1:54321";
    private const string LocalFrontendUrl = "https://127.0.0.1:3000";
    private const string DeployedSupabaseUrl = "https://aewdtsluyuopeezyzlcv.supabase.co";
    private const string DeployedFrontendUrl =
        "https://radiowash-web.mangopebble-8494c6d2.canadacentral.azurecontainerapps.io";

    [Fact]
    public void Validate_WithCompleteLocalConfiguration_DoesNotThrow()
    {
        var configuration = BuildConfiguration(LocalSupabaseUrl, "local-jwt-secret");

        LocalDevelopmentConfigurationGuard.Validate(configuration, LocalFrontendUrl);
    }

    [Fact]
    public void Validate_WhenContainerFallsBackToDeployedAppsettings_ThrowsNamingBothCauses()
    {
        // The exact shape of an uncontainerised .env: appsettings.json is the only config
        // present, so every value points at the deployed environment.
        var configuration = BuildConfiguration(
            DeployedSupabaseUrl,
            "the-deployed-projects-jwt-secret");

        var ex = Assert.Throws<InvalidOperationException>(
            () => LocalDevelopmentConfigurationGuard.Validate(configuration, DeployedFrontendUrl));

        // Both failures must be reported together — fixing only the one that happens to be
        // listed first would send the developer around the loop a second time.
        Assert.Contains("Supabase:PublicUrl", ex.Message);
        Assert.Contains("FrontendUrl", ex.Message);
        // The message has to carry the remedy, not just the diagnosis.
        Assert.Contains("SUPABASE_API_PORT", ex.Message);
        Assert.Contains("FRONTEND_URL", ex.Message);
        Assert.Contains(".env.example", ex.Message);
    }

    [Fact]
    public void Validate_WithMissingJwtSecret_ThrowsBecauseTokensCannotBeValidated()
    {
        var configuration = BuildConfiguration(LocalSupabaseUrl, jwtSecret: null);

        var ex = Assert.Throws<InvalidOperationException>(
            () => LocalDevelopmentConfigurationGuard.Validate(configuration, LocalFrontendUrl));

        Assert.Contains("SUPABASE_JWT_SECRET", ex.Message);
    }

    [Fact]
    public void Validate_WithMissingSupabaseUrl_ThrowsRatherThanDeferringToJwtBearerSetup()
    {
        var configuration = BuildConfiguration(supabasePublicUrl: null, jwtSecret: "local-jwt-secret");

        var ex = Assert.Throws<InvalidOperationException>(
            () => LocalDevelopmentConfigurationGuard.Validate(configuration, LocalFrontendUrl));

        Assert.Contains("Supabase:PublicUrl", ex.Message);
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("https://127.0.0.1:3000")]
    [InlineData("http://127.0.0.1:4200")]
    public void Validate_AcceptsAnyLocalFrontendOrigin(string frontendUrl)
    {
        // The guard's job is catching deployed values, not policing which local port or
        // hostname a developer serves the frontend on.
        var configuration = BuildConfiguration(LocalSupabaseUrl, "local-jwt-secret");

        LocalDevelopmentConfigurationGuard.Validate(configuration, frontendUrl);
    }

    [Fact]
    public void Validate_WithShiftedSupabasePort_DoesNotThrow()
    {
        // Running two Supabase projects side by side means non-default ports; that is a
        // supported local setup and must not trip the guard.
        var configuration = BuildConfiguration("http://127.0.0.1:54341", "local-jwt-secret");

        LocalDevelopmentConfigurationGuard.Validate(configuration, LocalFrontendUrl);
    }

    private static IConfiguration BuildConfiguration(string? supabasePublicUrl, string? jwtSecret)
    {
        var values = new Dictionary<string, string?>
        {
            ["Supabase:PublicUrl"] = supabasePublicUrl,
            ["Supabase:JwtSecret"] = jwtSecret,
        };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
