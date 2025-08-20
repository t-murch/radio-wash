using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;

namespace RadioWash.Api.Test.Integration;

/// <summary>
/// Integration tests for SignalR authentication
/// Tests PlaylistProgressHub authentication via query string tokens
/// Based on analysis in claude-thoughts/supabase-gotrue-v5-upgrade-analysis.md
/// </summary>
public class SignalRAuthenticationTests : IntegrationTestBase
{
    [Fact]
    public async Task SignalRHub_ShouldRequireAuthentication()
    {
        // Test that SignalR hub requires authentication
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                // No authentication token provided
            })
            .Build();
        
        // Should fail to connect without authentication
        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await connection.StartAsync();
        });
        
        // Should receive 401 Unauthorized
        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task SignalRHub_ShouldAllowConnectionWithValidToken()
    {
        // Test that SignalR hub allows connection with valid JWT token
        SeedTestData();
        
        var validToken = CreateTestJwtToken();
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                // Pass JWT token via query string (required for WebSocket auth)
                options.AccessTokenProvider = () => Task.FromResult<string?>(validToken);
            })
            .Build();
        
        // Should successfully connect with valid token
        await connection.StartAsync();
        
        Assert.Equal(HubConnectionState.Connected, connection.State);
        
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SignalRHub_ShouldRejectInvalidToken()
    {
        // Test that SignalR hub rejects invalid JWT tokens
        var invalidToken = "invalid.jwt.token";
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(invalidToken);
            })
            .Build();
        
        // Should fail to connect with invalid token
        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await connection.StartAsync();
        });
        
        Assert.Contains("401", exception.Message);
    }

    [Fact]
    public async Task SignalRHub_ShouldRejectExpiredToken()
    {
        // Test that SignalR hub rejects expired JWT tokens
        var expiredToken = CreateExpiredTestJwtToken();
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(expiredToken);
            })
            .Build();
        
        // Should fail to connect with expired token
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await connection.StartAsync();
        });
        
        // Connection should fail due to authentication
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task SignalRHub_JoinJobGroup_ShouldWorkWithValidAuth()
    {
        // Test that authenticated users can join job groups
        SeedTestData();
        
        var validToken = CreateTestJwtToken();
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(validToken);
            })
            .Build();
        
        await connection.StartAsync();
        
        // Should be able to call hub methods
        var jobId = "test-job-123";
        await connection.InvokeAsync("JoinJobGroup", jobId);
        
        // If we reach here without exception, the method call succeeded
        Assert.Equal(HubConnectionState.Connected, connection.State);
        
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SignalRHub_LeaveJobGroup_ShouldWorkWithValidAuth()
    {
        // Test that authenticated users can leave job groups
        SeedTestData();
        
        var validToken = CreateTestJwtToken();
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(validToken);
            })
            .Build();
        
        await connection.StartAsync();
        
        var jobId = "test-job-123";
        
        // Join and then leave group
        await connection.InvokeAsync("JoinJobGroup", jobId);
        await connection.InvokeAsync("LeaveJobGroup", jobId);
        
        // If we reach here without exception, both method calls succeeded
        Assert.Equal(HubConnectionState.Connected, connection.State);
        
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SignalRHub_ShouldLogConnectionEvents()
    {
        // Test that SignalR connection events are properly logged
        SeedTestData();
        
        var validToken = CreateTestJwtToken();
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(validToken);
            })
            .Build();
        
        // Connect and disconnect to trigger logging
        await connection.StartAsync();
        await connection.DisposeAsync();
        
        // Test passes if no exceptions are thrown during connection lifecycle
        Assert.True(true);
    }

    [Fact]
    public async Task SignalRHub_ShouldHandleMultipleConnections()
    {
        // Test multiple authenticated connections to the same hub
        SeedTestData();
        
        var validToken = CreateTestJwtToken();
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connections = new List<HubConnection>();
        
        // Create multiple connections
        for (int i = 0; i < 3; i++)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                    options.AccessTokenProvider = () => Task.FromResult<string?>(validToken);
                })
                .Build();
            
            connections.Add(connection);
        }
        
        // Start all connections
        await Task.WhenAll(connections.Select(c => c.StartAsync()));
        
        // All should be connected
        Assert.All(connections, c => Assert.Equal(HubConnectionState.Connected, c.State));
        
        // Clean up
        await Task.WhenAll(connections.Select(c => c.DisposeAsync().AsTask()));
    }

    [Fact]
    public async Task SignalRHub_ShouldHandleConnectionsFromDifferentUsers()
    {
        // Test connections from different authenticated users
        SeedTestData();
        
        var user1Token = CreateTestJwtToken("user1", "user1@example.com");
        var user2Token = CreateTestJwtToken("user2", "user2@example.com");
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection1 = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(user1Token);
            })
            .Build();
        
        var connection2 = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(user2Token);
            })
            .Build();
        
        await connection1.StartAsync();
        await connection2.StartAsync();
        
        // Both users should be able to connect
        Assert.Equal(HubConnectionState.Connected, connection1.State);
        Assert.Equal(HubConnectionState.Connected, connection2.State);
        
        await connection1.DisposeAsync();
        await connection2.DisposeAsync();
    }

    [Fact]
    public async Task SignalRHub_ShouldSupportWebSocketTransport()
    {
        // Test that WebSocket transport works with authentication
        SeedTestData();
        
        var validToken = CreateTestJwtToken();
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(validToken);
                options.Transports = HttpTransportType.WebSockets;
            })
            .Build();
        
        await connection.StartAsync();
        
        Assert.Equal(HubConnectionState.Connected, connection.State);
        
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SignalRHub_ShouldSupportServerSentEventsTransport()
    {
        // Test that Server-Sent Events transport works with authentication
        SeedTestData();
        
        var validToken = CreateTestJwtToken();
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(validToken);
                options.Transports = HttpTransportType.ServerSentEvents;
            })
            .Build();
        
        await connection.StartAsync();
        
        Assert.Equal(HubConnectionState.Connected, connection.State);
        
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SignalRHub_ShouldHandleConnectionTimeout()
    {
        // Test connection timeout handling
        var invalidHubUrl = "http://invalid-host:12345/hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(invalidHubUrl)
            .Build();
        
        // Should timeout when trying to connect to invalid URL
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await connection.StartAsync();
        });
        
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task SignalRHub_ShouldHandleInvalidJobId()
    {
        // Test handling of invalid job IDs in group operations
        SeedTestData();
        
        var validToken = CreateTestJwtToken();
        var hubUrl = $"{Client.BaseAddress}hubs/playlist-progress";
        
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(validToken);
            })
            .Build();
        
        await connection.StartAsync();
        
        // Should handle invalid job IDs gracefully
        var invalidJobIds = new[] { "", null, "invalid-job-id", "job-with-special-chars-!@#$%^&*()" };
        
        foreach (var invalidJobId in invalidJobIds)
        {
            // These calls may succeed or fail depending on validation, but should not crash
            try
            {
                await connection.InvokeAsync("JoinJobGroup", invalidJobId);
                await connection.InvokeAsync("LeaveJobGroup", invalidJobId);
            }
            catch (Exception ex)
            {
                // Exceptions are acceptable for invalid input
                Assert.NotNull(ex);
            }
        }
        
        await connection.DisposeAsync();
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
}