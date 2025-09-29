using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Middleware;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;
using System.Security.Claims;

namespace RadioWash.Api.Tests.Unit.Middleware;

/// <summary>
/// Unit tests for TokenRefreshMiddleware
/// Tests proactive token refresh logic, endpoint filtering, and error handling
/// </summary>
public class TokenRefreshMiddlewareTests
{
    private readonly Mock<ILogger<TokenRefreshMiddleware>> _mockLogger;
    private readonly Mock<RequestDelegate> _mockNext;
    private readonly Mock<IUserService> _mockUserService;
    private readonly Mock<IMusicTokenService> _mockTokenService;
    private readonly TokenRefreshMiddleware _middleware;

    public TokenRefreshMiddlewareTests()
    {
        _mockLogger = new Mock<ILogger<TokenRefreshMiddleware>>();
        _mockNext = new Mock<RequestDelegate>();
        _mockUserService = new Mock<IUserService>();
        _mockTokenService = new Mock<IMusicTokenService>();
        
        _middleware = new TokenRefreshMiddleware(_mockNext.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task InvokeAsync_WithUnauthenticatedRequest_CallsNextWithoutProcessing()
    {
        // Arrange
        var context = CreateHttpContext("/api/playlist/test");
        // No authentication claims

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockUserService.Verify(x => x.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WithNonApiEndpoint_CallsNextWithoutProcessing()
    {
        // Arrange
        var context = CreateHttpContext("/home");
        AddAuthenticatedUser(context, Guid.Parse("11111111-1111-1111-1111-111111111111"));

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockUserService.Verify(x => x.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/health")]
    [InlineData("/api/docs")]
    [InlineData("/other/endpoint")]
    public async Task InvokeAsync_WithNonTargetEndpoint_CallsNextWithoutProcessing(string path)
    {
        // Arrange
        var context = CreateHttpContext(path);
        AddAuthenticatedUser(context, Guid.Parse("11111111-1111-1111-1111-111111111111"));

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockUserService.Verify(x => x.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Theory]
    [InlineData("/api/playlist")]
    [InlineData("/api/playlist/test")]
    [InlineData("/api/jobs")]
    [InlineData("/api/jobs/123")]
    [InlineData("/api/spotify")]
    [InlineData("/api/spotify/playlists")]
    public async Task InvokeAsync_WithTargetEndpoint_ProcessesTokenRefresh(string path)
    {
        // Arrange
        var context = CreateHttpContext(path);
        var testUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        AddAuthenticatedUser(context, testUserId);

        var user = new UserDto { Id = 1, SupabaseId = testUserId.ToString() };
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(testUserId))
            .ReturnsAsync(user);

        var tokenInfo = new UserMusicToken
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5), // Expired
            EncryptedRefreshToken = "valid_refresh_token",
            RefreshFailureCount = 0,
            IsRevoked = false
        };
        _mockTokenService.Setup(x => x.GetTokenInfoAsync(1, "spotify"))
            .ReturnsAsync(tokenInfo);
        _mockTokenService.Setup(x => x.RefreshTokensAsync(1, "spotify"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockUserService.Verify(x => x.GetUserBySupabaseIdAsync(testUserId), Times.Once);
        _mockTokenService.Verify(x => x.RefreshTokensAsync(1, "spotify"), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidSupabaseId_DoesNotProcess()
    {
        // Arrange
        var context = CreateHttpContext("/api/playlist");
        var invalidUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        AddAuthenticatedUser(context, invalidUserId);

        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(invalidUserId))
            .ReturnsAsync((UserDto?)null);

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockTokenService.Verify(x => x.RefreshTokensAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WithTokenRefreshException_LogsWarningAndContinues()
    {
        // Arrange
        var context = CreateHttpContext("/api/spotify");
        var testUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        AddAuthenticatedUser(context, testUserId);

        var user = new UserDto { Id = 1, SupabaseId = testUserId.ToString() };
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(testUserId))
            .ReturnsAsync(user);

        var exception = new InvalidOperationException("Token refresh failed");
        _mockTokenService.Setup(x => x.GetTokenInfoAsync(1, "spotify"))
            .ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to refresh tokens in middleware")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithExpiredButNonRefreshableToken_DoesNotRefresh()
    {
        // Arrange
        var context = CreateHttpContext("/api/playlist");
        var testUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        AddAuthenticatedUser(context, testUserId);

        var user = new UserDto { Id = 1, SupabaseId = testUserId.ToString() };
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(testUserId))
            .ReturnsAsync(user);

        var tokenInfo = new UserMusicToken
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5), // Expired
            EncryptedRefreshToken = null, // Cannot refresh
            RefreshFailureCount = 0,
            IsRevoked = false
        };
        _mockTokenService.Setup(x => x.GetTokenInfoAsync(1, "spotify"))
            .ReturnsAsync(tokenInfo);

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockTokenService.Verify(x => x.RefreshTokensAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WithValidNonExpiredToken_DoesNotRefresh()
    {
        // Arrange
        var context = CreateHttpContext("/api/jobs");
        var testUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        AddAuthenticatedUser(context, testUserId);

        var user = new UserDto { Id = 1, SupabaseId = testUserId.ToString() };
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(testUserId))
            .ReturnsAsync(user);

        var tokenInfo = new UserMusicToken
        {
            ExpiresAt = DateTime.UtcNow.AddHours(1), // Not expired
            EncryptedRefreshToken = "valid_refresh_token",
            RefreshFailureCount = 0,
            IsRevoked = false
        };
        _mockTokenService.Setup(x => x.GetTokenInfoAsync(1, "spotify"))
            .ReturnsAsync(tokenInfo);

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockTokenService.Verify(x => x.RefreshTokensAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WithMissingSupabaseIdClaim_SkipsProcessing()
    {
        // Arrange
        var context = CreateHttpContext("/api/playlist");
        
        // Add authenticated user but without NameIdentifier claim
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("other_claim", "value"));
        context.User = new ClaimsPrincipal(identity);

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockUserService.Verify(x => x.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidGuidClaim_SkipsProcessing()
    {
        // Arrange
        var context = CreateHttpContext("/api/jobs");
        
        // Add authenticated user with invalid GUID format
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "not-a-valid-guid"));
        context.User = new ClaimsPrincipal(identity);

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockUserService.Verify(x => x.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_CaseInsensitiveEndpointMatching_ProcessesCorrectly()
    {
        // Arrange
        var context = CreateHttpContext("/API/PLAYLIST");
        var testUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        AddAuthenticatedUser(context, testUserId);

        var user = new UserDto { Id = 1, SupabaseId = testUserId.ToString() };
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(testUserId))
            .ReturnsAsync(user);

        var tokenInfo = new UserMusicToken
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5), // Expired
            EncryptedRefreshToken = "valid_refresh_token",
            RefreshFailureCount = 0,
            IsRevoked = false
        };
        _mockTokenService.Setup(x => x.GetTokenInfoAsync(1, "spotify"))
            .ReturnsAsync(tokenInfo);
        _mockTokenService.Setup(x => x.RefreshTokensAsync(1, "spotify"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockTokenService.Verify(x => x.RefreshTokensAsync(1, "spotify"), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithNoTokensForProvider_DoesNotRefresh()
    {
        // Arrange
        var context = CreateHttpContext("/api/playlist");
        var testUserId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        AddAuthenticatedUser(context, testUserId);

        var user = new UserDto { Id = 1, SupabaseId = testUserId.ToString() };
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(testUserId))
            .ReturnsAsync(user);

        _mockTokenService.Setup(x => x.GetTokenInfoAsync(1, "spotify"))
            .ReturnsAsync((UserMusicToken?)null);

        // Act
        await _middleware.InvokeAsync(context, _mockTokenService.Object, _mockUserService.Object);

        // Assert
        _mockNext.Verify(x => x(context), Times.Once);
        _mockTokenService.Verify(x => x.RefreshTokensAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    private static HttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void AddAuthenticatedUser(HttpContext context, Guid supabaseId)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, supabaseId.ToString()));
        context.User = new ClaimsPrincipal(identity);
    }
}