using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using RadioWash.Api.Infrastructure.Authentication;

namespace RadioWash.Api.Tests.Unit.Infrastructure;

/// <summary>
/// Covers the environment-gated JWT validation policy. The risk being guarded: a production
/// deploy that accepts HS256-signed tokens via a leaked Supabase JWT secret, or that accepts
/// arbitrary algorithms and is vulnerable to alg-confusion. Both rules must hold together.
/// </summary>
public class SupabaseJwtPolicyTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Test", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void AllowHs256_IsGatedToDevelopmentAndTestingEnvironments(string environmentName, bool expected)
    {
        var env = new FakeHostEnvironment(environmentName);

        Assert.Equal(expected, SupabaseJwtPolicy.AllowHs256(env));
    }

    [Fact]
    public void ValidAlgorithmsFor_Production_RestrictsToRs256AndEs256()
    {
        var env = new FakeHostEnvironment("Production");

        var algorithms = SupabaseJwtPolicy.ValidAlgorithmsFor(env);

        Assert.NotNull(algorithms);
        Assert.Equal(
            new[] { SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256 },
            algorithms);
        Assert.DoesNotContain(SecurityAlgorithms.HmacSha256, algorithms);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void ValidAlgorithmsFor_NonProduction_ReturnsNullSoValidatorAcceptsAllKnownAlgorithms(string environmentName)
    {
        // Microsoft.IdentityModel.Tokens treats a null/empty ValidAlgorithms as "accept any
        // algorithm the handler knows." For Development/Testing we rely on that so local HS256
        // tokens from `supabase start` still authenticate without per-environment config.
        var env = new FakeHostEnvironment(environmentName);

        Assert.Null(SupabaseJwtPolicy.ValidAlgorithmsFor(env));
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "RadioWash.Api.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
