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
/// Integration tests for JWKS-based JWT authentication.
/// These tests verify that the API correctly validates JWT tokens using
/// asymmetric keys (ES256) fetched from a JWKS endpoint.
/// </summary>
public class JwksAuthenticationTests : IClassFixture<AuthenticationTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly AuthenticationTestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private User? _testUser;

    public JwksAuthenticationTests(AuthenticationTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Seed a test user
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

        await dbContext.Database.MigrateAsync();

        _testUser = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            Email = "test@example.com",
            DisplayName = "Test User",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(_testUser);
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        await dbContext.Users.ExecuteDeleteAsync();
    }

    #region Valid Token Tests

    private Guid TestUserSupabaseGuid => Guid.Parse(_testUser!.SupabaseId);

    [Fact]
    public async Task AuthenticatedEndpoint_WithValidES256Token_ReturnsOk()
    {
        // Arrange
        var token = _factory.GenerateValidToken(TestUserSupabaseGuid, _testUser!.Email);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithValidToken_ReturnsCorrectUserData()
    {
        // Arrange
        var token = _factory.GenerateValidToken(TestUserSupabaseGuid, _testUser!.Email);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/auth/me");
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(user);
        Assert.Equal(_testUser.Email, user.Email);
    }

    [Fact]
    public async Task SpotifyTokensEndpoint_WithValidToken_AcceptsRequest()
    {
        // Arrange
        var token = _factory.GenerateValidToken(TestUserSupabaseGuid, _testUser!.Email);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
    public async Task AuthenticatedEndpoint_WithExpiredToken_ReturnsUnauthorized()
    {
        // Arrange
        var token = _factory.GenerateExpiredToken(TestUserSupabaseGuid);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithInvalidSignature_ReturnsUnauthorized()
    {
        // Arrange - Token signed with a different key
        var token = _factory.GenerateTokenWithInvalidSignature(TestUserSupabaseGuid);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithWrongIssuer_ReturnsUnauthorized()
    {
        // Arrange
        var token = _factory.GenerateTokenWithWrongIssuer(TestUserSupabaseGuid);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithWrongAudience_ReturnsUnauthorized()
    {
        // Arrange
        var token = _factory.GenerateTokenWithWrongAudience(TestUserSupabaseGuid);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithMalformedToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.valid.jwt.token");

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_WithEmptyToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "");

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region JWKS Specific Tests

    [Fact]
    public async Task Authentication_UsesKeyIdFromJwks_ToValidateToken()
    {
        // Arrange - The token's kid should match the key in JWKS
        var token = _factory.GenerateValidToken(TestUserSupabaseGuid);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert - Token with matching kid should be accepted
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authentication_RejectsToken_WhenKidNotInJwks()
    {
        // Arrange - Token signed with a key that has different kid
        var token = _factory.GenerateTokenWithInvalidSignature(TestUserSupabaseGuid);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert - Token with non-matching kid should be rejected
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Public Endpoint Tests

    [Fact]
    public async Task PublicEndpoint_WithNoToken_ReturnsOk()
    {
        // Arrange - no auth header
        _client.DefaultRequestHeaders.Authorization = null;

        // Act - Swagger/health endpoints should be accessible
        var response = await _client.GetAsync("/swagger/index.html");

        // Assert - Public endpoints should work without auth
        // Note: Might be 404 if swagger is disabled in test, that's fine
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    private record UserResponse(int Id, string Email, string? DisplayName);
}
