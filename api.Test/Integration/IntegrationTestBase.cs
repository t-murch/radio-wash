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

    protected IntegrationTestBase()
    {
        // Create the auth schema before the WebApplicationFactory starts Program.cs
        CreateSupabaseAuthSchemaBeforeStartup();
        
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

                    // Use dedicated test database
                    var testConnectionString = "Host=localhost;Port=15432;Database=radiowash_test;Username=postgres;Password=postgres";
                    
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
        
        // Get the test database context and ensure it's created
        DbContext = Scope.ServiceProvider.GetService<RadioWashDbContext>();
        if (DbContext != null)
        {
            DbContext.Database.EnsureCreated();
        }
    }

    /// <summary>
    /// Creates the auth schema and users table required by Supabase migrations
    /// </summary>
    private static void CreateSupabaseAuthSchemaBeforeStartup()
    {
        // Use the same connection string as defined in appsettings.Test.json
        var connectionString = "Host=localhost;Port=15432;Database=radiowash_test;Username=postgres;Password=postgres";
        
        try
        {
            using var connection = new Npgsql.NpgsqlConnection(connectionString);
            connection.Open();
            
            // Create auth schema
            using var cmd1 = new Npgsql.NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS auth;", connection);
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
            ", connection);
            cmd2.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the test setup
            Console.WriteLine($"Warning: Could not create auth schema: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a JWT token for testing authentication
    /// </summary>
    protected string CreateTestJwtToken(string userId = "test-user-id", string email = "test@example.com")
    {
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
            issuer: $"{Configuration["Supabase:PublicUrl"]}/auth/v1",
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
        
        // Ensure database is created and up-to-date
        DbContext.Database.EnsureCreated();
        
        // Clean up any existing test data
        var existingTestUser = DbContext.Users.FirstOrDefault(u => u.SupabaseId == "test-user-id");
        if (existingTestUser != null)
        {
            DbContext.Users.Remove(existingTestUser);
            DbContext.SaveChanges();
        }
        
        // Add test user that corresponds to JWT token
        var testUser = new RadioWash.Api.Models.Domain.User
        {
            SupabaseId = "test-user-id",
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
            var testUser = DbContext.Users.FirstOrDefault(u => u.SupabaseId == "test-user-id");
            if (testUser != null)
            {
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
        DbContext?.Dispose();
        Scope?.Dispose();
        Client?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}