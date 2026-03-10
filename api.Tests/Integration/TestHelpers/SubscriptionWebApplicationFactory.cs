using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Integration.TestHelpers;

/// <summary>
/// WebApplicationFactory configured for subscription integration tests.
/// Extends LocalSupabaseWebApplicationFactory with real Stripe test-mode credentials.
/// Only IEventUtility is mocked (to bypass webhook signature verification).
/// All other services (IPaymentService, IStripeSubscriptionClient, etc.) use real implementations.
/// </summary>
public class SubscriptionWebApplicationFactory : LocalSupabaseWebApplicationFactory
{
    private bool _migrated;
    private readonly SemaphoreSlim _migrationLock = new(1, 1);

    /// <summary>
    /// Ensures EF migrations and seed data have been applied.
    /// Safe to call multiple times — runs only once.
    /// </summary>
    public async Task EnsureMigratedAsync()
    {
        if (_migrated) return;

        await _migrationLock.WaitAsync();
        try
        {
            if (_migrated) return;

            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
            await dbContext.Database.MigrateAsync();

            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            await DatabaseSeeder.SeedSubscriptionPlansAsync(dbContext, config);

            _migrated = true;
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Call base configuration first (sets up Supabase, DB, auth, etc.)
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Load real Stripe test-mode credentials from appsettings.Testing.json
            var testingConfigPath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "appsettings.Testing.json");

            if (File.Exists(testingConfigPath))
            {
                config.AddJsonFile(testingConfigPath, optional: false);
            }

            // Environment variables override file config (for CI)
            config.AddEnvironmentVariables(prefix: "RADIOWASH_TEST_");
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace IEventUtility with test version that bypasses signature verification
            services.RemoveAll<IEventUtility>();
            services.AddSingleton<IEventUtility, TestEventUtility>();
        });
    }
}
