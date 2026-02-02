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
/// Integration tests for JWT authentication using the locally running Supabase stack.
/// These tests use real GoTrue-issued JWTs, providing maximum fidelity with production.
///
/// Prerequisites:
/// - Run `supabase start` before running these tests
/// - Supabase should be available at http://127.0.0.1:54321
/// </summary>
public class LocalSupabaseAuthenticationTests : IClassFixture<LocalSupabaseWebApplicationFactory>, IAsyncLifetime
{
    private readonly LocalSupabaseWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private string? _testUserEmail;
    private string? _testUserPassword;
    private string? _testUserToken;
    private string? _testUserId;

    public LocalSupabaseAuthenticationTests(LocalSupabaseWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Create a test user via GoTrue
        _testUserEmail = $"test-{Guid.NewGuid():N}@example.com";
        _testUserPassword = "TestPassword123!";

        var authResponse = await _factory.CreateTestUserAsync(_testUserEmail, _testUserPassword);
        _testUserToken = authResponse.access_token;
        _testUserId = authResponse.user?.id;

        // Wait a moment for the auth trigger to create the user in our Users table
        await Task.Delay(500);

        // Verify the user was created by the trigger
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

        var existingUser = await dbContext.Users.FirstOrDefaultAsync(u => u.SupabaseId == _testUserId);
        if (existingUser == null)
        {
            throw new InvalidOperationException(
                $"User with SupabaseId {_testUserId} was not created by the auth trigger. " +
                "Make sure the CreateAuthUserTrigger migration has been applied.");
        }
    }

    public async Task DisposeAsync()
    {
        // Delete the test user from Supabase
        if (_testUserId != null)
        {
            await _factory.DeleteTestUserAsync(_testUserId);
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
        Assert.Contains(_testUserEmail!, content);
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

        // Assert - Should not return 401 Unauthorized
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        // Should be OK (200) if successful
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        // Arrange - Tamper with the token by modifying the signature
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
        // Arrange - Generate a token with a completely different secret
        var fakeToken = GenerateFakeToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fakeToken);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Token Refresh Tests

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

    #endregion

    #region Multi-User Isolation Tests

    [Fact]
    public async Task MultipleUsers_EachHaveIsolatedAccess()
    {
        // Arrange - Create a second user
        var user2Email = $"test2-{Guid.NewGuid():N}@example.com";
        var user2Password = "TestPassword456!";
        var user2Auth = await _factory.CreateTestUserAsync(user2Email, user2Password);

        // Wait for trigger to create user
        await Task.Delay(500);

        try
        {
            // Act - Both users should be able to access their own data
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _testUserToken);
            var response1 = await _client.GetAsync("/api/auth/me");

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user2Auth.access_token);
            var response2 = await _client.GetAsync("/api/auth/me");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
            Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

            var content1 = await response1.Content.ReadAsStringAsync();
            var content2 = await response2.Content.ReadAsStringAsync();

            Assert.Contains(_testUserEmail!, content1);
            Assert.Contains(user2Email, content2);
            Assert.DoesNotContain(user2Email, content1);
            Assert.DoesNotContain(_testUserEmail!, content2);
        }
        finally
        {
            // Cleanup second user
            if (user2Auth.user?.id != null)
            {
                await _factory.DeleteTestUserAsync(user2Auth.user.id);
            }
        }
    }

    #endregion

    private static string GenerateFakeToken()
    {
        // Generate a JWT with a completely different secret
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            """{"alg":"HS256","typ":"JWT"}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $$$"""{"sub":"{{{Guid.NewGuid()}}}","aud":"authenticated","exp":{{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}},"iss":"http://127.0.0.1:54321/auth/v1"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var signature = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("fake-signature"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{header}.{payload}.{signature}";
    }
}
