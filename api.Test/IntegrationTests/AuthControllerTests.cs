using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using Xunit;

namespace RadioWash.Api.Test.IntegrationTests;

public class AuthControllerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;
    private readonly RadioWashDbContext _dbContext;

    public AuthControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                // Replace problematic services for testing
                var servicesToRemove = services.Where(d => 
                    d.ServiceType == typeof(DbContextOptions<RadioWashDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>) ||
                    d.ServiceType == typeof(RadioWashDbContext) ||
                    d.ServiceType == typeof(Supabase.Gotrue.Client) ||
                    d.ServiceType.FullName?.Contains("Hangfire") == true ||
                    d.ServiceType.FullName?.Contains("Npgsql") == true
                ).ToList();

                foreach (var descriptor in servicesToRemove)
                {
                    services.Remove(descriptor);
                }

                // Add test database with unique name
                services.AddDbContext<RadioWashDbContext>(options =>
                {
                    options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString());
                });

                // Mock Supabase.Gotrue.Client
                var mockSupabaseClient = new Mock<Supabase.Gotrue.Client>(
                    new Supabase.Gotrue.ClientOptions { Url = "http://test" }
                );
                services.AddSingleton(mockSupabaseClient.Object);

                // Add test authentication
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
            });
        });

        _client = _factory.CreateClient();
        _scope = _factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        
        // Ensure database schema is created for in-memory database
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _client.Dispose();
    }

    #region /auth/me Endpoint Tests

    [Fact]
    public async Task Me_WhenUserExists_ShouldReturnUserDto()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();
        var user = new User
        {
            SupabaseId = supabaseId.ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Set up test claims
        TestAuthHandler.TestClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, supabaseId.ToString())
        };

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var userDto = JsonConvert.DeserializeObject<UserDto>(content);
        
        Assert.NotNull(userDto);
        Assert.Equal(user.Id, userDto.Id);
        Assert.Equal(user.SupabaseId, userDto.SupabaseId);
        Assert.Equal(user.DisplayName, userDto.DisplayName);
        Assert.Equal(user.Email, userDto.Email);
        Assert.Equal(user.PrimaryProvider, userDto.PrimaryProvider);
    }

    [Fact]
    public async Task Me_WhenUserExistsWithProviderData_ShouldReturnUserWithProviderData()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();
        var user = new User
        {
            SupabaseId = supabaseId.ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var providerData = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123",
            ProviderMetadata = "{\"displayName\":\"Spotify User\"}"
        };
        await _dbContext.UserProviderData.AddAsync(providerData);
        await _dbContext.SaveChangesAsync();

        // Set up test claims
        TestAuthHandler.TestClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, supabaseId.ToString())
        };

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var userDto = JsonConvert.DeserializeObject<UserDto>(content);
        
        Assert.NotNull(userDto);
        Assert.Single(userDto.ProviderData);
        Assert.Equal("spotify", userDto.ProviderData.First().Provider);
        Assert.Equal("spotify123", userDto.ProviderData.First().ProviderId);
    }

    [Fact]
    public async Task Me_WhenUserDoesNotExist_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentSupabaseId = Guid.NewGuid();
        
        // Set up test claims with non-existent user ID
        TestAuthHandler.TestClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, nonExistentSupabaseId.ToString())
        };

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("User not found", content);
    }

    [Fact]
    public async Task Me_WhenNoUserIdClaim_ShouldReturnUnauthorized()
    {
        // Arrange
        TestAuthHandler.TestClaims = new List<Claim>(); // No claims

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("User ID not found in token", content);
    }

    [Fact]
    public async Task Me_WhenInvalidUserIdClaim_ShouldReturnUnauthorized()
    {
        // Arrange
        TestAuthHandler.TestClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "invalid-guid")
        };

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("User ID not found in token", content);
    }

    [Fact]
    public async Task Me_WhenNotAuthenticated_ShouldReturnUnauthorized()
    {
        // Arrange
        TestAuthHandler.TestClaims = null; // No authentication

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Multi-Provider Scenarios

    [Fact]
    public async Task Me_WhenUserHasMultipleProviders_ShouldReturnAllProviderData()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();
        var user = new User
        {
            SupabaseId = supabaseId.ToString(),
            DisplayName = "Multi-Provider User",
            Email = "multiuser@example.com",
            PrimaryProvider = "spotify"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var spotifyProvider = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123",
            ProviderMetadata = "{\"displayName\":\"Spotify User\"}"
        };
        var appleProvider = new UserProviderData
        {
            UserId = user.Id,
            Provider = "apple",
            ProviderId = "apple456",
            ProviderMetadata = "{\"displayName\":\"Apple User\"}"
        };
        await _dbContext.UserProviderData.AddRangeAsync(spotifyProvider, appleProvider);
        await _dbContext.SaveChangesAsync();

        // Set up test claims
        TestAuthHandler.TestClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, supabaseId.ToString())
        };

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var userDto = JsonConvert.DeserializeObject<UserDto>(content);
        
        Assert.NotNull(userDto);
        Assert.Equal(2, userDto.ProviderData.Count);
        Assert.Contains(userDto.ProviderData, pd => pd.Provider == "spotify" && pd.ProviderId == "spotify123");
        Assert.Contains(userDto.ProviderData, pd => pd.Provider == "apple" && pd.ProviderId == "apple456");
        Assert.Equal("spotify", userDto.PrimaryProvider);
    }

    [Fact]
    public async Task Me_WhenEmailOnlyUser_ShouldReturnUserWithNullPrimaryProvider()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();
        var user = new User
        {
            SupabaseId = supabaseId.ToString(),
            DisplayName = "Email User",
            Email = "emailuser@example.com",
            PrimaryProvider = null // Email/password only user
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Set up test claims
        TestAuthHandler.TestClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, supabaseId.ToString())
        };

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var userDto = JsonConvert.DeserializeObject<UserDto>(content);
        
        Assert.NotNull(userDto);
        Assert.Null(userDto.PrimaryProvider);
        Assert.Empty(userDto.ProviderData);
        Assert.Equal("emailuser@example.com", userDto.Email);
    }

    #endregion
}

// Test authentication handler for integration tests
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public static List<Claim>? TestClaims { get; set; }

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (TestClaims == null)
        {
            return Task.FromResult(AuthenticateResult.Fail("No test claims provided"));
        }

        var identity = new ClaimsIdentity(TestClaims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}