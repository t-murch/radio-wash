using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace RadioWash.Api.Test.Integration;

/// <summary>
/// Base class for integration tests with test server setup and common utilities
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly WebApplicationFactory<Program> Factory;
    protected readonly HttpClient Client;
    protected readonly IServiceScope Scope;
    protected readonly IConfiguration Configuration;
    protected readonly RadioWashDbContext? DbContext;
    protected readonly string TestDatabaseName;
    protected readonly string TestUserId;

    protected IntegrationTestBase()
    {
        // Generate unique identifiers for this test instance
        TestDatabaseName = $"radiowash_test_{Guid.NewGuid():N}";
        TestUserId = $"test-user-{Guid.NewGuid():N}";
        
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Set the environment to Test - this will load appsettings.Test.json
                builder.UseEnvironment("Test");

                builder.ConfigureServices(services =>
                {
                    // Remove the existing DbContext registration
                    services.RemoveAll<DbContextOptions<RadioWashDbContext>>();
                    services.RemoveAll<RadioWashDbContext>();

                    // Create test database with auth schema (for Supabase compatibility)
                    CreateTestDatabaseWithAuthSchema();

                    // Use database-per-test for true isolation (simplified Supabase stack)
                    var testConnectionString = $"Host=localhost;Port=15432;Database={TestDatabaseName};Username=postgres;Password=postgres";
                    
                    services.AddDbContext<RadioWashDbContext>(options =>
                        options.UseNpgsql(testConnectionString));

                    // Register repositories for integration tests
                    services.AddScoped<IUserRepository, UserRepository>();
                    services.AddScoped<IUserProviderDataRepository, UserProviderDataRepository>();
                    services.AddScoped<IUserMusicTokenRepository, UserMusicTokenRepository>();
                    services.AddScoped<ICleanPlaylistJobRepository, CleanPlaylistJobRepository>();
                    services.AddScoped<ITrackMappingRepository, TrackMappingRepository>();
                });
            });

        Client = Factory.CreateClient();
        Scope = Factory.Services.CreateScope();
        Configuration = Scope.ServiceProvider.GetRequiredService<IConfiguration>();
        
        // Get the test database context
        DbContext = Scope.ServiceProvider.GetService<RadioWashDbContext>();
    }

    /// <summary>
    /// Creates the test database and auth schema required by Supabase migrations
    /// </summary>
    private void CreateTestDatabaseWithAuthSchema()
    {
        try
        {
            // Use simplified Supabase database but create test database for isolation
            var systemConnectionString = "Host=localhost;Port=15432;Database=postgres;Username=postgres;Password=postgres";
            using var systemConnection = new Npgsql.NpgsqlConnection(systemConnectionString);
            systemConnection.Open();
            
            // Create the test database
            using var createDbCmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{TestDatabaseName}\";", systemConnection);
            createDbCmd.ExecuteNonQuery();
            
            // Now connect to the new test database to create the auth schema
            var testConnectionString = $"Host=localhost;Port=15432;Database={TestDatabaseName};Username=postgres;Password=postgres";
            using var testConnection = new Npgsql.NpgsqlConnection(testConnectionString);
            testConnection.Open();
            
            // Create auth schema (required by Supabase migrations)
            using var cmd1 = new Npgsql.NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS auth;", testConnection);
            cmd1.ExecuteNonQuery();
            
            // Create minimal auth.users table with required columns for the migration
            using var cmd2 = new Npgsql.NpgsqlCommand(@"
                CREATE TABLE IF NOT EXISTS auth.users (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    email TEXT UNIQUE,
                    raw_user_meta_data JSONB,
                    created_at TIMESTAMPTZ DEFAULT NOW(),
                    updated_at TIMESTAMPTZ DEFAULT NOW()
                );
            ", testConnection);
            cmd2.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the test setup - the database might already exist
            Console.WriteLine($"Warning: Could not create test database or auth schema: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a JWT token for testing authentication
    /// </summary>
    protected string CreateTestJwtToken(string? userId = null, string email = "test@example.com")
    {
        userId ??= TestUserId; // Use unique test user ID if none provided
        
        var jwtSecret = Configuration["Supabase:JwtSecret"]!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Email, email),
            new Claim("aud", "authenticated"),
            new Claim("role", "authenticated"),
            new Claim("sub", userId)
        };

        var token = new JwtSecurityToken(
            issuer: Configuration["Supabase:JwtIssuer"],
            audience: "authenticated",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Creates an HttpRequestMessage with authentication header
    /// </summary>
    protected HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string uri, string? token = null)
    {
        var request = new HttpRequestMessage(method, uri);
        token ??= CreateTestJwtToken();
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    /// Seeds test data into the database
    /// </summary>
    protected RadioWash.Api.Models.Domain.User SeedTestData()
    {
        if (DbContext == null) throw new InvalidOperationException("DbContext is null");
        
        // Ensure database is created and up-to-date by running migrations
        try
        {
            DbContext.Database.Migrate();
        }
        catch
        {
            // If migration fails, fall back to EnsureCreated as backup
            DbContext.Database.EnsureCreated();
        }
        
        // Check if test user already exists and return it
        var existingTestUser = DbContext.Users.FirstOrDefault(u => u.SupabaseId == TestUserId);
        if (existingTestUser != null)
        {
            return existingTestUser;
        }
        
        // Add test user that corresponds to JWT token
        var testUser = new RadioWash.Api.Models.Domain.User
        {
            SupabaseId = TestUserId,
            Email = "test@example.com",
            DisplayName = "Test User", // Add the required DisplayName field
            CreatedAt = DateTime.UtcNow
        };

        DbContext.Users.Add(testUser);
        DbContext.SaveChanges();
        
        return testUser; // Return the user with the assigned ID
    }

    /// <summary>
    /// Cleans up test data from the database
    /// </summary>
    protected void CleanupTestData()
    {
        if (DbContext == null) return;
        
        try
        {
            // Remove test user and associated data
            var testUser = DbContext.Users.FirstOrDefault(u => u.SupabaseId == TestUserId);
            if (testUser != null)
            {
                // Remove associated clean playlist jobs and track mappings
                var userJobs = DbContext.CleanPlaylistJobs.Where(j => j.UserId == testUser.Id);
                foreach (var job in userJobs)
                {
                    var trackMappings = DbContext.TrackMappings.Where(tm => tm.JobId == job.Id);
                    DbContext.TrackMappings.RemoveRange(trackMappings);
                }
                DbContext.CleanPlaylistJobs.RemoveRange(userJobs);
                
                // Remove associated music tokens
                var userTokens = DbContext.UserMusicTokens.Where(t => t.UserId == testUser.Id);
                DbContext.UserMusicTokens.RemoveRange(userTokens);
                
                // Remove associated provider data
                var userProviderData = DbContext.UserProviderData.Where(p => p.UserId == testUser.Id);
                DbContext.UserProviderData.RemoveRange(userProviderData);
                
                // Remove user
                DbContext.Users.Remove(testUser);
                DbContext.SaveChanges();
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    public virtual void Dispose()
    {
        try
        {
            // Drop the test database completely to prevent accumulation
            DbContext?.Dispose();
            Scope?.Dispose();
            Client?.Dispose();
            Factory?.Dispose();
            
            // Drop the test database using system connection
            var systemConnectionString = "Host=localhost;Port=15432;Database=postgres;Username=postgres;Password=postgres";
            using var systemConnection = new Npgsql.NpgsqlConnection(systemConnectionString);
            systemConnection.Open();
            
            // Terminate any connections to the test database first
            using var terminateCmd = new Npgsql.NpgsqlCommand($@"
                SELECT pg_terminate_backend(pid) 
                FROM pg_stat_activity 
                WHERE datname = '{TestDatabaseName}' AND pid <> pg_backend_pid();", systemConnection);
            terminateCmd.ExecuteNonQuery();
            
            // Drop the test database
            using var dropDbCmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{TestDatabaseName}\";", systemConnection);
            dropDbCmd.ExecuteNonQuery();
        }
        catch
        {
            // Ignore cleanup errors - database might not exist or be in use
        }
        
        GC.SuppressFinalize(this);
    }
}