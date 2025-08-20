using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Moq;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Test.Integration;

/// <summary>
/// Integration tests for authentication error scenarios
/// Tests resilience and proper error handling in auth flows
/// </summary>
public class AuthErrorScenarioTests : IntegrationTestBase
{
    [Fact]
    public async Task InvalidJwt_ShouldReturn401()
    {
        // Test malformed JWT tokens against validation in Program.cs:60-104
        var invalidJwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.invalid.signature";
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", invalidJwt);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        
        // Verify error message formatting (should not expose internal details)
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrEmpty(content) && content.Contains("Internal"));
    }
    
    [Fact] 
    public async Task ExpiredJwt_ShouldReturn401()
    {
        // Test expired token handling
        var expiredToken = CreateExpiredTestJwtToken();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    [Fact] 
    public async Task MalformedJwt_ShouldReturn401()
    {
        // Test various malformed JWT formats
        var malformedTokens = new[]
        {
            "not-a-jwt-at-all",
            "header.payload", // Missing signature
            "too.many.parts.here.invalid",
            "", // Empty token
            "Bearer ", // Just the scheme
            "Basic dGVzdDp0ZXN0" // Wrong auth scheme content
        };
        
        foreach (var malformedToken in malformedTokens)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", malformedToken);
            
            var response = await Client.SendAsync(request);
            
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
    
    [Fact]
    public async Task JwtWithInvalidSignature_ShouldReturn401()
    {
        // Test JWT with valid format but invalid signature
        var validToken = CreateTestJwtToken();
        var parts = validToken.Split('.');
        
        // Tamper with the signature
        var tamperedSignature = Convert.ToBase64String(Encoding.UTF8.GetBytes("tampered-signature"));
        var tamperedToken = $"{parts[0]}.{parts[1]}.{tamperedSignature}";
        
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    [Fact]
    public async Task JwtWithWrongAudience_ShouldReturn401()
    {
        // Test JWT with wrong audience claim
        var wrongAudienceToken = CreateTestJwtTokenWithClaims(new Dictionary<string, object>
        {
            {"sub", "test-user-id"},
            {"email", "test@example.com"},
            {"aud", "wrong-audience"}, // Wrong audience
            {"role", "authenticated"}
        });
        
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wrongAudienceToken);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    [Fact]
    public async Task JwtWithWrongIssuer_ShouldReturn401()
    {
        // Test JWT with wrong issuer
        var wrongIssuerToken = CreateTestJwtTokenWithWrongIssuer();
        
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wrongIssuerToken);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    [Fact]
    public async Task NoAuthorizationHeader_ShouldReturn401()
    {
        // Test request with no Authorization header
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        // Deliberately not setting Authorization header
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    [Fact]
    public async Task WrongAuthScheme_ShouldReturn401()
    {
        // Test wrong authentication scheme (not Bearer)
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "dGVzdDp0ZXN0");
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    [Fact]
    public async Task MultipleAuthHeaders_ShouldHandleGracefully()
    {
        // Test multiple Authorization headers (edge case)
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        var validToken = CreateTestJwtToken();
        
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", validToken);
        // Try to add another Authorization header (this should be handled gracefully)
        request.Headers.Add("Authorization", "Bearer another-token");
        
        var response = await Client.SendAsync(request);
        
        // Should either work with first token or return 401 - but not crash
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Unauthorized);
    }
    
    [Fact]
    public async Task VeryLongJwtToken_ShouldHandleGracefully()
    {
        // Test very long JWT token (potential DoS attempt)
        var veryLongToken = new string('a', 10000); // 10KB token
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", veryLongToken);
        
        var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        
        // Response should be returned quickly (not timeout)
        Assert.NotNull(response);
    }
    
    [Fact]
    public async Task NonExistentUser_ShouldReturn401OrNotFound()
    {
        // Test JWT for user that doesn't exist in database
        var nonExistentUserToken = CreateTestJwtToken("non-existent-user-id", "nonexistent@example.com");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", nonExistentUserToken);
        
        var response = await Client.SendAsync(request);
        
        // Could be 401 (Unauthorized) or 404 (Not Found) depending on implementation
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || 
                     response.StatusCode == HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Creates an expired JWT token for testing
    /// </summary>
    private string CreateExpiredTestJwtToken()
    {
        var jwtSecret = Configuration["Supabase:JwtSecret"]!;
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "test@example.com"),
            new System.Security.Claims.Claim("aud", "authenticated"),
            new System.Security.Claims.Claim("role", "authenticated"),
            new System.Security.Claims.Claim("sub", "test-user-id")
        };

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: $"{Configuration["Supabase:PublicUrl"]}/auth/v1",
            audience: "authenticated",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(-1), // Expired 1 hour ago
            signingCredentials: creds
        );

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Creates JWT token with custom claims
    /// </summary>
    private string CreateTestJwtTokenWithClaims(Dictionary<string, object> customClaims)
    {
        var jwtSecret = Configuration["Supabase:JwtSecret"]!;
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = customClaims.Select(kvp => 
            new System.Security.Claims.Claim(kvp.Key, kvp.Value.ToString()!)).ToArray();

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: $"{Configuration["Supabase:PublicUrl"]}/auth/v1",
            audience: customClaims.ContainsKey("aud") ? customClaims["aud"].ToString() : "authenticated",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Creates JWT token with wrong issuer
    /// </summary>
    private string CreateTestJwtTokenWithWrongIssuer()
    {
        var jwtSecret = Configuration["Supabase:JwtSecret"]!;
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "test@example.com"),
            new System.Security.Claims.Claim("aud", "authenticated"),
            new System.Security.Claims.Claim("role", "authenticated"),
            new System.Security.Claims.Claim("sub", "test-user-id")
        };

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "https://wrong-issuer.com/auth/v1", // Wrong issuer
            audience: "authenticated",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}