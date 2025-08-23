using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Models.Domain;
using Moq;
using Moq.Protected;

namespace RadioWash.Api.Test.Integration;

/// <summary>
/// Integration tests for token management functionality
/// Tests TokenRefreshMiddleware and SupabaseUserProviderTokenService
/// </summary>
public class TokenManagementIntegrationTests : IntegrationTestBase
{
    [Fact]
    public void SupabaseUserProviderTokenService_ShouldBeRegistered()
    {
        // Test that SupabaseUserProviderTokenService is properly registered
        var service = Scope.ServiceProvider.GetService<IUserProviderTokenService>();
        
        Assert.NotNull(service);
        Assert.IsType<RadioWash.Api.Services.Implementations.SupabaseUserProviderTokenService>(service);
    }

    [Fact]
    public async Task AuthController_SpotifyTokens_ShouldStoreTokensCorrectly()
    {
        // Test POST /api/auth/spotify/tokens endpoint
        SeedTestData();
        
        var token = CreateTestJwtToken();
        var tokenRequest = new
        {
            accessToken = "spotify-access-token",
            refreshToken = "spotify-refresh-token",
            expiresAt = DateTime.UtcNow.AddHours(1)
        };
        
        var json = JsonSerializer.Serialize(tokenRequest);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/spotify/tokens")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthController_SpotifyTokens_ShouldRequireAuthentication()
    {
        // Test that token storage requires authentication
        var tokenRequest = new
        {
            accessToken = "spotify-access-token",
            refreshToken = "spotify-refresh-token",
            expiresAt = DateTime.UtcNow.AddHours(1)
        };
        
        var json = JsonSerializer.Serialize(tokenRequest);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/spotify/tokens")
        {
            Content = content
        };
        // No authorization header
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthController_SpotifyTokens_ShouldValidateRequestBody()
    {
        // Test validation of token request body
        SeedTestData();
        
        var token = CreateTestJwtToken();
        var invalidTokenRequest = new
        {
            // Missing required fields
            invalidField = "invalid-value"
        };
        
        var json = JsonSerializer.Serialize(invalidTokenRequest);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/spotify/tokens")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthController_Logout_ShouldHandleValidRequest()
    {
        // Test POST /api/auth/logout endpoint
        SeedTestData();
        
        var token = CreateTestJwtToken();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthController_Logout_ShouldRequireAuthentication()
    {
        // Test that logout requires authentication
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        // No authorization header
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenRefreshMiddleware_ShouldHandleExpiredTokens()
    {
        // Test middleware behavior with expired tokens
        // Note: This test verifies the middleware is properly configured
        // Real token refresh testing would require mocking Spotify API
        
        SeedTestUserWithExpiredToken();
        
        var token = CreateTestJwtToken();
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/auth/spotify/status", token);
        
        var response = await Client.SendAsync(request);
        
        // Should either succeed (if refresh works) or return proper error
        Assert.True(response.StatusCode == HttpStatusCode.OK || 
                     response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_ShouldWorkWithValidToken()
    {
        // Test that authenticated endpoints work with valid tokens
        SeedTestData();
        
        var token = CreateTestJwtToken();
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/auth/me", token);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_ShouldRejectInvalidUser()
    {
        // Test authenticated endpoint with valid JWT but non-existent user
        // Don't seed test data, so user won't exist in database
        
        var token = CreateTestJwtToken("non-existent-user", "nonexistent@example.com");
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/auth/me", token);
        
        var response = await Client.SendAsync(request);
        
        // Should return 401 or 404 depending on implementation
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || 
                     response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MultipleSimultaneousRequests_ShouldHandleCorrectly()
    {
        // Test concurrent authenticated requests
        SeedTestData();
        
        var token = CreateTestJwtToken();
        var tasks = new List<Task<HttpResponseMessage>>();
        
        // Create 10 simultaneous requests
        for (int i = 0; i < 10; i++)
        {
            var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/auth/me", token);
            tasks.Add(Client.SendAsync(request));
        }
        
        var responses = await Task.WhenAll(tasks);
        
        // All requests should succeed
        Assert.True(responses.All(r => r.StatusCode == HttpStatusCode.OK));
        
        // Cleanup
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task CorsHeaders_ShouldBePresent()
    {
        // Test that CORS headers are properly configured for auth endpoints
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/me");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization");
        
        var response = await Client.SendAsync(request);
        
        // CORS preflight should be handled
        Assert.True(response.StatusCode == HttpStatusCode.OK || 
                     response.StatusCode == HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Seeds test data with a user that has expired tokens
    /// </summary>
    private void SeedTestUserWithExpiredToken()
    {
        var testUser = new User
        {
            SupabaseId = "test-user-id",
            Email = "test@example.com",
            CreatedAt = DateTime.UtcNow
        };

        DbContext.Users.Add(testUser);

        // Add expired music token
        var expiredToken = new UserMusicToken
        {
            User = testUser,
            Provider = "Spotify",
            EncryptedAccessToken = "encrypted-expired-token",
            EncryptedRefreshToken = "encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1), // Expired
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };

        DbContext.UserMusicTokens.Add(expiredToken);
        DbContext.SaveChanges();
    }
}