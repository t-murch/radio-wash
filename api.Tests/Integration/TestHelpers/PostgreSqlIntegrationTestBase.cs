using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioWash.Api.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace RadioWash.Api.Tests.Integration.TestHelpers;

/// <summary>
/// Base class for integration tests using PostgreSQL in Docker
/// </summary>
public abstract class PostgreSqlIntegrationTestBase : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgresContainer;
    protected readonly RadioWashDbContext _dbContext;
    protected readonly IServiceProvider _serviceProvider;

    protected PostgreSqlIntegrationTestBase()
    {
        // Start PostgreSQL container
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:15")
            .WithDatabase("radiowash_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCleanUp(true)
            .Build();

        // Wait for container to start
        _postgresContainer.StartAsync().GetAwaiter().GetResult();

        // Setup services
        var services = new ServiceCollection();
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<RadioWashDbContext>();

        // Apply migrations
        _dbContext.Database.Migrate();
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Add DbContext with test database
        services.AddDbContext<RadioWashDbContext>(options =>
            options.UseNpgsql(_postgresContainer.GetConnectionString()));

        // Add default implementations for abstractions
        services.AddSingleton<RadioWash.Api.Services.Interfaces.IDateTimeProvider, TestDateTimeProvider>();
        services.AddSingleton<RadioWash.Api.Services.Interfaces.IRandomProvider, TestRandomProvider>();
    }

    /// <summary>
    /// Clears all data from the test database
    /// </summary>
    protected async Task ClearDatabaseAsync()
    {
        await _dbContext.WebhookRetries.ExecuteDeleteAsync();
        await _dbContext.ProcessedWebhookEvents.ExecuteDeleteAsync();
        await _dbContext.UserSubscriptions.ExecuteDeleteAsync();
        await _dbContext.PlaylistSyncHistory.ExecuteDeleteAsync();
        await _dbContext.PlaylistSyncConfigs.ExecuteDeleteAsync();
        await _dbContext.SubscriptionPlans.ExecuteDeleteAsync();
        await _dbContext.TrackMappings.ExecuteDeleteAsync();
        await _dbContext.CleanPlaylistJobs.ExecuteDeleteAsync();
        await _dbContext.UserMusicTokens.ExecuteDeleteAsync();
        await _dbContext.UserProviderData.ExecuteDeleteAsync();
        await _dbContext.Users.ExecuteDeleteAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _dbContext?.Dispose();
        _serviceProvider?.GetService<IServiceScope>()?.Dispose();
        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }
}