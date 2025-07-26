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
using Supabase.Gotrue;
using System.Security.Claims;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

public class AuthControllerTests : IDisposable
{
    private readonly Mock<ILogger<AuthController>> _loggerMock;
    private readonly Mock<IMemoryCache> _memoryCacheMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<IWebHostEnvironment> _environmentMock;
    // Note: Supabase client is not easily mockable and not used in the tested methods
    private readonly Mock<IUserService> _userServiceMock;
    private readonly Mock<IMusicTokenService> _musicTokenServiceMock;
    private readonly AuthController _sut;

    public AuthControllerTests()
    {
        _loggerMock = new Mock<ILogger<AuthController>>();
        _memoryCacheMock = new Mock<IMemoryCache>();
        _configurationMock = new Mock<IConfiguration>();
        _environmentMock = new Mock<IWebHostEnvironment>();
        // Supabase client cannot be easily mocked due to constructor constraints
        _userServiceMock = new Mock<IUserService>();
        _musicTokenServiceMock = new Mock<IMusicTokenService>();

        // Setup default configuration values
        _configurationMock.Setup(c => c["Spotify:ClientId"]).Returns("fake-test-client-id");
        _configurationMock.Setup(c => c["Spotify:ClientSecret"]).Returns("fake-test-client-secret");
        _configurationMock.Setup(c => c["BackendUrl"]).Returns("http://127.0.0.1:5159");
        _configurationMock.Setup(c => c["FrontendUrl"]).Returns("http://127.0.0.1:3000");

        _sut = new AuthController(
            _loggerMock.Object,
            _memoryCacheMock.Object,
            _configurationMock.Object,
            _environmentMock.Object,
            null!, // Supabase client - not used in tested methods
            _userServiceMock.Object,
            _musicTokenServiceMock.Object);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #region SpotifyLogin Tests

    [Fact]
    public void SpotifyLogin_WhenConfigurationValid_ShouldRedirectToSpotifyAuth()
    {
        // Act
        var result = _sut.SpotifyLogin();

        // Assert
        var redirectResult = Assert.IsType<RedirectResult>(result);
        Assert.Contains("accounts.spotify.com/authorize", redirectResult.Url);
        Assert.Contains("client_id=fake-test-client-id", redirectResult.Url);
        Assert.Contains("response_type=code", redirectResult.Url);
        Assert.Contains("redirect_uri=http%3a%2f%2f127.0.0.1%3a5159%2fapi%2fauth%2fspotify%2fcallback", redirectResult.Url);
    }

    [Fact]
    public void SpotifyLogin_WhenClientIdMissing_ShouldRedirectToErrorPage()
    {
        // Arrange
        _configurationMock.Setup(c => c["Spotify:ClientId"]).Returns((string?)null);

        // Act
        var result = _sut.SpotifyLogin();

        // Assert
        var redirectResult = Assert.IsType<RedirectResult>(result);
        Assert.Contains("auth?error=server_error", redirectResult.Url);
    }

    #endregion

    #region Me Tests

    [Fact]
    public async Task Me_WhenUserAuthenticated_ShouldReturnUserData()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "test");
        var user = new ClaimsPrincipal(identity);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var userDto = new UserDto
        {
            Id = 1,
            SupabaseId = userId.ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            ProviderData = new List<UserProviderDataDto>()
        };

        _userServiceMock.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(userDto);

        // Act
        var result = await _sut.Me();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedUser = Assert.IsType<UserDto>(okResult.Value);
        Assert.Equal(userDto.Id, returnedUser.Id);
        Assert.Equal(userDto.DisplayName, returnedUser.DisplayName);
        Assert.Equal(userDto.Email, returnedUser.Email);
    }

    [Fact]
    public async Task Me_WhenUserNotAuthenticated_ShouldReturnUnauthorized()
    {
        // Arrange
        var user = new ClaimsPrincipal();
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await _sut.Me();

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.NotNull(unauthorizedResult.Value);
    }

    [Fact]
    public async Task Me_WhenUserNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "test");
        var user = new ClaimsPrincipal(identity);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        _userServiceMock.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync((UserDto?)null);

        // Act
        var result = await _sut.Me();

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    #endregion

    #region SpotifyConnectionStatus Tests

    [Fact]
    public async Task SpotifyConnectionStatus_WhenUserNotAuthenticated_ShouldReturnUnauthorized()
    {
        // Arrange
        var user = new ClaimsPrincipal();
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await _sut.SpotifyConnectionStatus();

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.NotNull(unauthorizedResult.Value);
    }

    [Fact]
    public async Task SpotifyConnectionStatus_WhenUserHasValidTokens_ShouldReturnConnectedStatus()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "test");
        var user = new ClaimsPrincipal(identity);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var userDto = new UserDto
        {
            Id = 1,
            SupabaseId = userId.ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            ProviderData = new List<UserProviderDataDto>()
        };

        _userServiceMock.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(userDto);

        _musicTokenServiceMock.Setup(x => x.HasValidTokensAsync(1, "spotify"))
            .ReturnsAsync(true);

        var tokenInfo = new UserMusicToken
        {
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastRefreshAt = DateTime.UtcNow.AddHours(-1),
            EncryptedRefreshToken = "encrypted-refresh-token"
        };

        _musicTokenServiceMock.Setup(x => x.GetTokenInfoAsync(1, "spotify"))
            .ReturnsAsync(tokenInfo);

        // Act
        var result = await _sut.SpotifyConnectionStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        
        // Use anonymous type checking instead of reflection
        var response = okResult.Value;
        var responseJson = System.Text.Json.JsonSerializer.Serialize(response);
        Assert.Contains("\"connected\":true", responseJson);
        Assert.Contains("\"canRefresh\":true", responseJson);
    }

    [Fact]
    public async Task SpotifyConnectionStatus_WhenUserNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "test");
        var user = new ClaimsPrincipal(identity);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        _userServiceMock.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync((UserDto?)null);

        // Act
        var result = await _sut.SpotifyConnectionStatus();

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public async Task Logout_WhenUserAuthenticated_ShouldReturnSuccessWithoutRevokingTokens()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "test");
        var user = new ClaimsPrincipal(identity);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await _sut.Logout(revokeTokens: false);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var responseJson = System.Text.Json.JsonSerializer.Serialize(response);
        Assert.Contains("\"success\":true", responseJson);
        Assert.Contains("\"tokensRevoked\":false", responseJson);
    }

    [Fact]
    public async Task Logout_WhenRevokeTokensRequested_ShouldRevokeTokensAndReturnSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "test");
        var user = new ClaimsPrincipal(identity);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var userDto = new UserDto
        {
            Id = 1,
            SupabaseId = userId.ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            ProviderData = new List<UserProviderDataDto>()
        };

        _userServiceMock.Setup(x => x.GetUserBySupabaseIdAsync(userId))
            .ReturnsAsync(userDto);

        _musicTokenServiceMock.Setup(x => x.RevokeTokensAsync(1, "spotify"))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.Logout(revokeTokens: true);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var responseJson = System.Text.Json.JsonSerializer.Serialize(response);
        Assert.Contains("\"success\":true", responseJson);
        Assert.Contains("\"tokensRevoked\":true", responseJson);

        // Verify that RevokeTokensAsync was called
        _musicTokenServiceMock.Verify(x => x.RevokeTokensAsync(1, "spotify"), Times.Once);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void SpotifyLogin_WhenBackendUrlNotConfigured_ShouldUseDefaultHttpUrl()
    {
        // Arrange
        _configurationMock.Setup(c => c["BackendUrl"]).Returns((string?)null);

        // Act
        var result = _sut.SpotifyLogin();

        // Assert
        var redirectResult = Assert.IsType<RedirectResult>(result);
        Assert.Contains("127.0.0.1%3a5159", redirectResult.Url);
        Assert.Contains("http%3a%2f%2f", redirectResult.Url); // HTTP, not HTTPS
    }

    [Fact]
    public void SpotifyLogin_WhenFrontendUrlNotConfigured_ShouldUseDefaultForErrors()
    {
        // Arrange
        _configurationMock.Setup(c => c["FrontendUrl"]).Returns((string?)null);
        _configurationMock.Setup(c => c["Spotify:ClientId"]).Returns((string?)null); // Force error path

        // Act
        var result = _sut.SpotifyLogin();

        // Assert
        var redirectResult = Assert.IsType<RedirectResult>(result);
        Assert.Contains("127.0.0.1:3000", redirectResult.Url);
        Assert.StartsWith("http://", redirectResult.Url); // HTTP, not HTTPS
    }

    #endregion
}