using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using RadioWash.Api.Middleware;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;

namespace RadioWash.Api.Test.UnitTests;

/// <summary>
/// Unit tests for TokenRefreshMiddleware
/// Tests proactive token refresh functionality and error handling
/// </summary>
public class TokenRefreshMiddlewareTests
{
    private readonly Mock<RequestDelegate> _nextMock;
    private readonly Mock<ILogger<TokenRefreshMiddleware>> _loggerMock;
    private readonly Mock<IMusicTokenService> _musicTokenServiceMock;
    private readonly Mock<IUserService> _userServiceMock;
    private readonly TokenRefreshMiddleware _middleware;

    public TokenRefreshMiddlewareTests()
    {
        _nextMock = new Mock<RequestDelegate>();
        _loggerMock = new Mock<ILogger<TokenRefreshMiddleware>>();
        _musicTokenServiceMock = new Mock<IMusicTokenService>();
        _userServiceMock = new Mock<IUserService>();
        _middleware = new TokenRefreshMiddleware(_nextMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext_ForAllRequests()
    {
        // Arrange
        var context = CreateHttpContext();

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldSkipTokenRefresh_ForUnauthenticatedRequests()
    {
        // Arrange
        var context = CreateHttpContext(authenticated: false, path: "/api/playlist/123");

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _userServiceMock.Verify(s => s.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
        _musicTokenServiceMock.Verify(s => s.GetTokenInfoAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldSkipTokenRefresh_ForNonMusicServicePaths()
    {
        // Arrange
        var context = CreateHttpContext(authenticated: true, path: "/api/auth/me");

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _userServiceMock.Verify(s => s.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
        _musicTokenServiceMock.Verify(s => s.GetTokenInfoAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Theory]
    [InlineData("/api/playlist/123")]
    [InlineData("/api/jobs/456")]
    [InlineData("/api/spotify/status")]
    [InlineData("/API/PLAYLIST/UPPERCASE")]
    public async Task InvokeAsync_ShouldCheckTokens_ForMusicServicePaths(string path)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(authenticated: true, path: path, userIdClaim: userId.ToString());
        var user = new UserDto { Id = 1, SupabaseId = userId.ToString(), Email = "test@example.com", DisplayName = "Test User" };
        
        _userServiceMock.Setup(s => s.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        
        _musicTokenServiceMock.Setup(s => s.GetTokenInfoAsync(user.Id, "spotify"))
            .ReturnsAsync((UserMusicToken?)null);

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _userServiceMock.Verify(s => s.GetUserBySupabaseIdAsync(userId), Times.Once);
        _musicTokenServiceMock.Verify(s => s.GetTokenInfoAsync(user.Id, "spotify"), Times.Once);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldRefreshExpiredTokens_WhenTokensAreExpiredAndRefreshable()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: userId.ToString());
        var user = new UserDto { Id = 1, SupabaseId = userId.ToString(), Email = "test@example.com", DisplayName = "Test User" };
        var expiredToken = new UserMusicToken
        {
            UserId = user.Id,
            Provider = "spotify",
            EncryptedAccessToken = "encrypted-token",
            EncryptedRefreshToken = "encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10), // Expired 10 minutes ago
            RefreshFailureCount = 0,
            IsRevoked = false
        };
        
        _userServiceMock.Setup(s => s.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        
        _musicTokenServiceMock.Setup(s => s.GetTokenInfoAsync(user.Id, "spotify"))
            .ReturnsAsync(expiredToken);
        
        _musicTokenServiceMock.Setup(s => s.RefreshTokensAsync(user.Id, "spotify"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _musicTokenServiceMock.Verify(s => s.RefreshTokensAsync(user.Id, "spotify"), Times.Once);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotRefreshTokens_WhenTokensAreNotExpired()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: userId.ToString());
        var user = new UserDto { Id = 1, SupabaseId = userId.ToString(), Email = "test@example.com", DisplayName = "Test User" };
        var validToken = new UserMusicToken
        {
            UserId = user.Id,
            Provider = "spotify",
            EncryptedAccessToken = "encrypted-token",
            EncryptedRefreshToken = "encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1), // Valid for 1 hour
            RefreshFailureCount = 0,
            IsRevoked = false
        };
        
        _userServiceMock.Setup(s => s.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        
        _musicTokenServiceMock.Setup(s => s.GetTokenInfoAsync(user.Id, "spotify"))
            .ReturnsAsync(validToken);

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _musicTokenServiceMock.Verify(s => s.RefreshTokensAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotRefreshTokens_WhenTokensCannotBeRefreshed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: userId.ToString());
        var user = new UserDto { Id = 1, SupabaseId = userId.ToString(), Email = "test@example.com", DisplayName = "Test User" };
        var expiredNonRefreshableToken = new UserMusicToken
        {
            UserId = user.Id,
            Provider = "spotify",
            EncryptedAccessToken = "encrypted-token",
            EncryptedRefreshToken = null, // No refresh token available
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10), // Expired 10 minutes ago
            RefreshFailureCount = 0,
            IsRevoked = false
        };
        
        _userServiceMock.Setup(s => s.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        
        _musicTokenServiceMock.Setup(s => s.GetTokenInfoAsync(user.Id, "spotify"))
            .ReturnsAsync(expiredNonRefreshableToken);

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _musicTokenServiceMock.Verify(s => s.RefreshTokensAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldHandleInvalidUserIdClaim_Gracefully()
    {
        // Arrange
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: "invalid-guid");

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _userServiceMock.Verify(s => s.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldHandleMissingUserIdClaim_Gracefully()
    {
        // Arrange
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: null);

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _userServiceMock.Verify(s => s.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldHandleUserNotFound_Gracefully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: userId.ToString());
        
        _userServiceMock.Setup(s => s.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync((UserDto?)null);

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _musicTokenServiceMock.Verify(s => s.GetTokenInfoAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldHandleTokenServiceException_Gracefully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: userId.ToString());
        var user = new UserDto { Id = 1, SupabaseId = userId.ToString(), Email = "test@example.com", DisplayName = "Test User" };
        
        _userServiceMock.Setup(s => s.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        
        _musicTokenServiceMock.Setup(s => s.GetTokenInfoAsync(user.Id, "spotify"))
            .ThrowsAsync(new Exception("Token service error"));

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        // Should continue processing despite exception
        _nextMock.Verify(next => next(context), Times.Once);
        
        // Should log the warning
        VerifyLoggerWarning("Failed to refresh tokens in middleware");
    }

    [Fact]
    public async Task InvokeAsync_ShouldHandleRefreshTokenException_Gracefully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: userId.ToString());
        var user = new UserDto { Id = 1, SupabaseId = userId.ToString(), Email = "test@example.com", DisplayName = "Test User" };
        var expiredToken = new UserMusicToken
        {
            UserId = user.Id,
            Provider = "spotify",
            EncryptedAccessToken = "encrypted-token",
            EncryptedRefreshToken = "encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10), // Expired 10 minutes ago
            RefreshFailureCount = 0,
            IsRevoked = false
        };
        
        _userServiceMock.Setup(s => s.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        
        _musicTokenServiceMock.Setup(s => s.GetTokenInfoAsync(user.Id, "spotify"))
            .ReturnsAsync(expiredToken);
        
        _musicTokenServiceMock.Setup(s => s.RefreshTokensAsync(user.Id, "spotify"))
            .ThrowsAsync(new Exception("Refresh failed"));

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        // Should continue processing despite exception
        _nextMock.Verify(next => next(context), Times.Once);
        
        // Should log the warning
        VerifyLoggerWarning("Failed to refresh tokens in middleware");
    }

    [Fact]
    public async Task InvokeAsync_ShouldLogTokenRefresh_WhenRefreshingTokens()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: userId.ToString());
        var user = new UserDto { Id = 1, SupabaseId = userId.ToString(), Email = "test@example.com", DisplayName = "Test User" };
        var expiredToken = new UserMusicToken
        {
            UserId = user.Id,
            Provider = "spotify",
            EncryptedAccessToken = "encrypted-token",
            EncryptedRefreshToken = "encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10), // Expired 10 minutes ago
            RefreshFailureCount = 0,
            IsRevoked = false
        };
        
        _userServiceMock.Setup(s => s.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        
        _musicTokenServiceMock.Setup(s => s.GetTokenInfoAsync(user.Id, "spotify"))
            .ReturnsAsync(expiredToken);
        
        _musicTokenServiceMock.Setup(s => s.RefreshTokensAsync(user.Id, "spotify"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        VerifyLoggerInformation("Proactively refreshing Spotify tokens for user");
    }

    [Fact]
    public async Task InvokeAsync_ShouldHandleNoUserMusicToken_Gracefully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateHttpContext(authenticated: true, path: "/api/playlist/123", userIdClaim: userId.ToString());
        var user = new UserDto { Id = 1, SupabaseId = userId.ToString(), Email = "test@example.com", DisplayName = "Test User" };
        
        _userServiceMock.Setup(s => s.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        
        _musicTokenServiceMock.Setup(s => s.GetTokenInfoAsync(user.Id, "spotify"))
            .ReturnsAsync((UserMusicToken?)null);

        // Act
        await _middleware.InvokeAsync(context, _musicTokenServiceMock.Object, _userServiceMock.Object);

        // Assert
        _musicTokenServiceMock.Verify(s => s.RefreshTokensAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _nextMock.Verify(next => next(context), Times.Once);
    }

    private HttpContext CreateHttpContext(bool authenticated = true, string path = "/", string? userIdClaim = "test-user-id")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        if (authenticated)
        {
            var claims = new List<Claim>();
            if (userIdClaim != null)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userIdClaim));
            }
            
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            context.User = new ClaimsPrincipal(identity);
        }

        return context;
    }

    private void VerifyLoggerWarning(string expectedMessage)
    {
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private void VerifyLoggerInformation(string expectedMessage)
    {
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}