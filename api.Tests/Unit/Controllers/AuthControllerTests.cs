using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Controllers;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for AuthController
/// Tests authentication endpoints, token management, and user profile operations
/// </summary>
public class AuthControllerTests
{
    private readonly Mock<ILogger<AuthController>> _mockLogger;
    private readonly Mock<IMemoryCache> _mockMemoryCache;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IWebHostEnvironment> _mockEnvironment;
    private readonly Mock<IUserService> _mockUserService;
    private readonly Mock<IMusicTokenService> _mockMusicTokenService;
    private readonly AuthController _authController;

    public AuthControllerTests()
    {
        _mockLogger = new Mock<ILogger<AuthController>>();
        _mockMemoryCache = new Mock<IMemoryCache>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockEnvironment = new Mock<IWebHostEnvironment>();
        _mockUserService = new Mock<IUserService>();
        _mockMusicTokenService = new Mock<IMusicTokenService>();

        _authController = new AuthController(
            _mockLogger.Object,
            _mockMemoryCache.Object,
            _mockConfiguration.Object,
            _mockEnvironment.Object,
            _mockUserService.Object,
            _mockMusicTokenService.Object);

        // Setup default configuration values
        _mockConfiguration.Setup(x => x["FrontendUrl"]).Returns("http://127.0.0.1:3000");
        _mockConfiguration.Setup(x => x["BackendUrl"]).Returns("http://127.0.0.1:5159");
    }

    [Fact]
    public async Task StoreSpotifyTokens_WithValidRequest_ReturnsOk()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = CreateTestUserDto();
        var request = new SpotifyTokenRequest
        {
            AccessToken = "access_token_123",
            RefreshToken = "refresh_token_123"
        };

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);

        var storedToken = CreateTestUserMusicToken();
        _mockMusicTokenService.Setup(x => x.StoreTokensAsync(
            user.Id, "spotify", request.AccessToken, request.RefreshToken, 3600,
            It.IsAny<string[]>(), null))
            .ReturnsAsync(storedToken);

        // Act
        var result = await _authController.StoreSpotifyTokens(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        _mockMusicTokenService.Verify(x => x.StoreTokensAsync(
            user.Id, "spotify", request.AccessToken, request.RefreshToken, 3600,
            It.Is<string[]>(scopes => scopes.Contains("user-read-private") && scopes.Contains("playlist-modify-private")),
            null), Times.Once);
    }

    [Fact]
    public async Task StoreSpotifyTokens_WithNoUserIdClaim_ReturnsUnauthorized()
    {
        // Arrange
        var request = new SpotifyTokenRequest
        {
            AccessToken = "access_token_123",
            RefreshToken = "refresh_token_123"
        };

        SetupUnauthenticatedUser();

        // Act
        var result = await _authController.StoreSpotifyTokens(request);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        var response = unauthorizedResult.Value;
        Assert.NotNull(response);

        _mockUserService.Verify(x => x.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
        _mockMusicTokenService.Verify(x => x.StoreTokensAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<int>(), It.IsAny<string[]>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task StoreSpotifyTokens_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new SpotifyTokenRequest
        {
            AccessToken = "access_token_123",
            RefreshToken = "refresh_token_123"
        };

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync((UserDto?)null);

        // Act
        var result = await _authController.StoreSpotifyTokens(request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        var response = notFoundResult.Value;
        Assert.NotNull(response);

        _mockMusicTokenService.Verify(x => x.StoreTokensAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<int>(), It.IsAny<string[]>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task StoreSpotifyTokens_WithException_ReturnsInternalServerError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = CreateTestUserDto();
        var request = new SpotifyTokenRequest
        {
            AccessToken = "access_token_123",
            RefreshToken = "refresh_token_123"
        };

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        _mockMusicTokenService.Setup(x => x.StoreTokensAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<int>(), It.IsAny<string[]>(), It.IsAny<object>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _authController.StoreSpotifyTokens(request);

        // Assert
        var serverErrorResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, serverErrorResult.StatusCode);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error storing Spotify tokens")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SpotifyConnectionStatus_WithValidUser_ReturnsConnectionInfo()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = CreateTestUserDto();
        var tokenInfo = CreateTestUserMusicToken();

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        _mockMusicTokenService.Setup(x => x.HasValidTokensAsync(user.Id, "spotify"))
            .ReturnsAsync(true);
        _mockMusicTokenService.Setup(x => x.GetTokenInfoAsync(user.Id, "spotify"))
            .ReturnsAsync(tokenInfo);

        // Act
        var result = await _authController.SpotifyConnectionStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var responseType = response.GetType();
        var connectedProperty = responseType.GetProperty("connected");
        var connectedValue = connectedProperty?.GetValue(response);
        Assert.Equal(true, connectedValue);
    }

    [Fact]
    public async Task SpotifyConnectionStatus_WithInvalidUser_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync((UserDto?)null);

        // Act
        var result = await _authController.SpotifyConnectionStatus();

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task Me_WithValidUser_ReturnsUserProfile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = CreateTestUserDto();

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);

        // Act
        var result = await _authController.Me();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedUser = Assert.IsType<UserDto>(okResult.Value);
        Assert.Equal(user.Id, returnedUser.Id);
        Assert.Equal(user.Email, returnedUser.Email);
        Assert.Equal(user.DisplayName, returnedUser.DisplayName);
    }

    [Fact]
    public async Task Me_WithInvalidUser_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync((UserDto?)null);

        // Act
        var result = await _authController.Me();

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task Me_WithNoUserClaim_ReturnsUnauthorized()
    {
        // Arrange
        SetupUnauthenticatedUser();

        // Act
        var result = await _authController.Me();

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.NotNull(unauthorizedResult.Value);

        _mockUserService.Verify(x => x.GetUserBySupabaseIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Logout_WithoutRevokeTokens_ReturnsSuccessWithoutRevocation()
    {
        // Arrange
        var userId = Guid.NewGuid();

        SetupAuthenticatedUser(userId);

        // Act
        var result = await _authController.Logout(revokeTokens: false);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var responseType = response.GetType();
        var tokensRevokedProperty = responseType.GetProperty("tokensRevoked");
        var tokensRevokedValue = tokensRevokedProperty?.GetValue(response);
        Assert.Equal(false, tokensRevokedValue);

        _mockMusicTokenService.Verify(x => x.RevokeTokensAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Logout_WithRevokeTokens_ReturnsSuccessWithRevocation()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = CreateTestUserDto();

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);

        // Act
        var result = await _authController.Logout(revokeTokens: true);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var responseType = response.GetType();
        var tokensRevokedProperty = responseType.GetProperty("tokensRevoked");
        var tokensRevokedValue = tokensRevokedProperty?.GetValue(response);
        Assert.Equal(true, tokensRevokedValue);

        _mockMusicTokenService.Verify(x => x.RevokeTokensAsync(user.Id, "spotify"), Times.Once);
    }

    [Fact]
    public async Task Logout_WithRevokeTokensButInvalidUser_ReturnsSuccessWithoutRevocation()
    {
        // Arrange
        var userId = Guid.NewGuid();

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync((UserDto?)null);

        // Act
        var result = await _authController.Logout(revokeTokens: true);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var responseType = response.GetType();
        var tokensRevokedProperty = responseType.GetProperty("tokensRevoked");
        var tokensRevokedValue = tokensRevokedProperty?.GetValue(response);
        Assert.Equal(false, tokensRevokedValue);

        _mockMusicTokenService.Verify(x => x.RevokeTokensAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Logout_WithException_ReturnsInternalServerError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = CreateTestUserDto();

        SetupAuthenticatedUser(userId);
        _mockUserService.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(user);
        _mockMusicTokenService.Setup(x => x.RevokeTokensAsync(user.Id, "spotify"))
            .ThrowsAsync(new Exception("Revocation error"));

        // Act
        var result = await _authController.Logout(revokeTokens: true);

        // Assert
        var serverErrorResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, serverErrorResult.StatusCode);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error during logout")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private void SetupAuthenticatedUser(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };

        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var principal = new ClaimsPrincipal(identity);

        _authController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal
            }
        };
    }

    private void SetupUnauthenticatedUser()
    {
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);

        _authController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal
            }
        };
    }

    private static UserDto CreateTestUserDto()
    {
        return new UserDto
        {
            Id = 1,
            SupabaseId = "sb_test",
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "email",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
            ProviderData = new List<UserProviderDataDto>()
        };
    }

    private static UserMusicToken CreateTestUserMusicToken()
    {
        return new UserMusicToken
        {
            Id = 1,
            UserId = 1,
            Provider = "spotify",
            EncryptedAccessToken = "encrypted_access_token",
            EncryptedRefreshToken = "encrypted_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Scopes = "[]",
            ProviderMetadata = "{}",
            RefreshFailureCount = 0,
            LastRefreshAt = null,
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        };
    }
}