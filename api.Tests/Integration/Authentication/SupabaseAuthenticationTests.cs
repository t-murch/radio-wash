using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Integration.TestHelpers;

namespace RadioWash.Api.Tests.Integration.Authentication;

/// <summary>
/// Integration tests for JWT authentication using a real Supabase stack.
/// These tests use real GoTrue-issued JWTs, providing maximum fidelity with production.
/// </summary>
[Collection("SupabaseTests")]
public class SupabaseAuthenticationTests : IClassFixture<SupabaseTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly SupabaseTestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private string? _testUserEmail;
    private string? _testUserPassword;
    private string? _testUserToken;
    private string? _testUserId;

    public SupabaseAuthenticationTests(SupabaseTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Create a test user via GoTrue
        _testUserEmail = $"test-{Guid.NewGuid():N}@example.com";
        _testUserPassword = "TestPassword123!";

        var authResponse = await _factory.Supabase.CreateTestUserAsync(_testUserEmail, _testUserPassword);
        _testUserToken = authResponse.AccessToken;
        _testUserId = authResponse.User?.Id;

        // The user should be automatically created in our Users table via the auth trigger
        // If not, we need to create it manually for the API to work
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

        // Check if user was created by trigger, if not create manually
        var existingUser = await dbContext.Users.FirstOrDefaultAsync(u => u.SupabaseId == _testUserId);
        if (existingUser == null && _testUserId != null)
        {
            var user = new User
            {
                SupabaseId = _testUserId,
                Email = _testUserEmail,
                DisplayName = "Test User",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        // Cleanup test user from database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

        if (_testUserId != null)
        {
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.SupabaseId == _testUserId);
            if (user != null)
            {
                dbContext.Users.Remove(user);
                await dbContext.SaveChangesAsync();
            }
        }
    }

    #region Valid Token Tests

    [Fact]
    public async Task AuthenticatedEndpoint_WithRealSupabaseToken_ReturnsOk()
    {
        // Arrange - Use token from real GoTrue
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _testUserToken);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithRealToken_ReturnsCorrectUserData()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _testUserToken);

        // Act
        var response = await _client.GetAsync("/api/auth/me");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_testUserEmail, content);
    }

    [Fact]
    public async Task SpotifyTokensEndpoint_WithRealToken_AcceptsRequest()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _testUserToken);

        var tokenRequest = new
        {
            accessToken = "spotify_access_token_123",
            refreshToken = "spotify_refresh_token_456",
            expiresAt = DateTime.UtcNow.AddHours(1).ToString("O")
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/spotify/tokens", tokenRequest);

        // Assert
        // Should not return 401 Unauthorized - authentication should pass
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SpotifyStatus_WithRealToken_ReturnsStatus()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _testUserToken);

        // Act
        var response = await _client.GetAsync("/api/auth/spotify/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Invalid Token Tests

    [Fact]
    public async Task AuthenticatedEndpoint_WithNoToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithMalformedToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.valid.jwt");

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithTamperedToken_ReturnsUnauthorized()
    {
        // Arrange - Tamper with the token by modifying a character
        var tamperedToken = _testUserToken![..^5] + "XXXXX";
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithTokenFromDifferentSecret_ReturnsUnauthorized()
    {
        // Arrange - Generate a token with a different secret
        var fakeToken = GenerateFakeToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fakeToken);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Fresh Token Tests

    [Fact]
    public async Task SignIn_ReturnsValidToken_ThatWorksWithApi()
    {
        // Arrange - Sign in to get a fresh token
        var freshToken = await _factory.SignInAndGetTokenAsync(_testUserEmail!, _testUserPassword!);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MultipleUsers_EachHaveIsolatedAccess()
    {
        // Arrange - Create a second user
        var user2Email = $"test2-{Guid.NewGuid():N}@example.com";
        var user2Password = "TestPassword456!";
        var user2Auth = await _factory.Supabase.CreateTestUserAsync(user2Email, user2Password);

        // Create user2 in our database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        var user2 = new User
        {
            SupabaseId = user2Auth.User!.Id,
            Email = user2Email,
            DisplayName = "Test User 2",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        dbContext.Users.Add(user2);
        await dbContext.SaveChangesAsync();

        // Act - Both users should be able to access their own data
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _testUserToken);
        var response1 = await _client.GetAsync("/api/auth/me");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user2Auth.AccessToken);
        var response2 = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var content1 = await response1.Content.ReadAsStringAsync();
        var content2 = await response2.Content.ReadAsStringAsync();

        Assert.Contains(_testUserEmail, content1);
        Assert.Contains(user2Email, content2);
        Assert.DoesNotContain(user2Email, content1);
        Assert.DoesNotContain(_testUserEmail, content2);

        // Cleanup
        dbContext.Users.Remove(user2);
        await dbContext.SaveChangesAsync();
    }

    #endregion

    private static string GenerateFakeToken()
    {
        // Generate a JWT with a completely different secret
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            """{"alg":"HS256","typ":"JWT"}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $$$"""{"sub":"{{{Guid.NewGuid()}}}","aud":"authenticated","exp":{{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}},"iss":"http://localhost:8000/auth/v1"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var signature = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("fake-signature"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{header}.{payload}.{signature}";
    }
}
