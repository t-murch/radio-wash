using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Integration.TestHelpers;

/// <summary>
/// WebApplicationFactory that uses the locally running Supabase stack (via `supabase start`).
/// This provides maximum fidelity with the development environment and production.
///
/// Prerequisites:
/// - Run `supabase start` before running tests
/// - Supabase should be available at http://127.0.0.1:54321
/// </summary>
public class LocalSupabaseWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly LocalSupabaseTestFixture _supabase = new();

    public LocalSupabaseTestFixture Supabase => _supabase;
    public string JwtIssuer => _supabase.JwtIssuer;
    public const string JwtAudience = "authenticated";

    public async Task InitializeAsync()
    {
        await _supabase.InitializeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Use existing Supabase database - migrations should already be applied
                ["SkipMigrations"] = "true",
                ["Supabase:PublicUrl"] = _supabase.SupabaseUrl,
                ["Supabase:Url"] = _supabase.SupabaseUrl,
                ["Supabase:JwtSecret"] = _supabase.JwtSecret,
                ["Supabase:AnonKey"] = _supabase.AnonKey,
                ["Supabase:ServiceRoleKey"] = _supabase.ServiceRoleKey,
                ["Supabase:JwtIssuer"] = _supabase.JwtIssuer,
                ["Stripe:SecretKey"] = "sk_test_fake",
                ["Stripe:WebhookSecret"] = "whsec_fake",
                ["Stripe:PricePlanId"] = "price_fake",
                ["Stripe:PublishableKey"] = "pk_test_fake",
                ["ConnectionStrings:DefaultConnection"] = _supabase.DatabaseConnectionString,
                ["FrontendUrl"] = "http://localhost:3000"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Remove existing DbContext registration
            services.RemoveAll<DbContextOptions<RadioWashDbContext>>();
            services.RemoveAll<RadioWashDbContext>();

            // Use the local Supabase PostgreSQL database
            services.AddDbContext<RadioWashDbContext>(options =>
                options.UseNpgsql(_supabase.DatabaseConnectionString));

            // JWT authentication uses JWKS from Supabase - configured in Program.cs
            // We need to configure the backchannel to allow HTTP calls to local Supabase
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.RequireHttpsMetadata = false;
                // Use a standard HttpClient for backchannel (JWKS fetching)
                // This allows the test to reach the local Supabase instance
                options.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
            });

            // Replace time provider with test version
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider, TestDateTimeProvider>();

            // Replace random provider with test version
            services.RemoveAll<IRandomProvider>();
            services.AddSingleton<IRandomProvider, TestRandomProvider>();
        });
    }

    /// <summary>
    /// Creates a test user via GoTrue and returns the access token.
    /// </summary>
    public async Task<string> CreateUserAndGetTokenAsync(string email, string password)
    {
        var response = await _supabase.CreateTestUserAsync(email, password);
        return response.access_token ?? throw new InvalidOperationException("No access token returned");
    }

    /// <summary>
    /// Signs in an existing user and returns the access token.
    /// </summary>
    public async Task<string> SignInAndGetTokenAsync(string email, string password)
    {
        var response = await _supabase.SignInAsync(email, password);
        return response.access_token ?? throw new InvalidOperationException("No access token returned");
    }

    /// <summary>
    /// Creates a test user and returns the full auth response.
    /// </summary>
    public async Task<LocalSupabaseTestFixture.AuthResponse> CreateTestUserAsync(string email, string password)
    {
        return await _supabase.CreateTestUserAsync(email, password);
    }

    /// <summary>
    /// Deletes a test user (cleanup).
    /// </summary>
    public async Task DeleteTestUserAsync(string userId)
    {
        await _supabase.DeleteTestUserAsync(userId);
    }

    public new async Task DisposeAsync()
    {
        await _supabase.DisposeAsync();
        await base.DisposeAsync();
    }
}
