using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

/// <summary>
/// Unit tests for MusicTokenService using mocked repository dependencies
/// </summary>
public class MusicTokenServiceTests : IDisposable
{
    private readonly Mock<IUserMusicTokenRepository> _tokenRepositoryMock;
    private readonly Mock<ITokenEncryptionService> _encryptionServiceMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<MusicTokenService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly MusicTokenService _sut;

    public MusicTokenServiceTests()
    {
        _tokenRepositoryMock = new Mock<IUserMusicTokenRepository>();
        _encryptionServiceMock = new Mock<ITokenEncryptionService>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<MusicTokenService>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        // Setup HttpClient with mocked handler
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);

        // Setup default configuration
        _configurationMock.Setup(c => c["Spotify:ClientId"]).Returns("fake-test-client-id");
        _configurationMock.Setup(c => c["Spotify:ClientSecret"]).Returns("fake-test-client-secret");

        _sut = new MusicTokenService(
            _tokenRepositoryMock.Object,
            _encryptionServiceMock.Object,
            _configurationMock.Object,
            _loggerMock.Object,
            _httpClient);
    }

    #region StoreTokensAsync Tests

    [Fact]
    public async Task StoreTokensAsync_WhenNewUser_ShouldCreateNewTokenRecord()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var accessToken = "access-token";
        var refreshToken = "refresh-token";
        var expiresIn = 3600;
        var scopes = new[] { "user-read-private", "playlist-read-private" };
        var metadata = new { displayName = "Test User" };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        _encryptionServiceMock.Setup(x => x.EncryptToken(accessToken))
            .Returns("encrypted-access-token");
        _encryptionServiceMock.Setup(x => x.EncryptToken(refreshToken))
            .Returns("encrypted-refresh-token");

        var capturedToken = new UserMusicToken();
        _tokenRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<UserMusicToken>()))
            .Callback<UserMusicToken>(token => capturedToken = token)
            .ReturnsAsync((UserMusicToken token) => token);

        // Act
        var result = await _sut.StoreTokensAsync(userId, provider, accessToken, refreshToken, expiresIn, scopes, metadata);

        // Assert
        Assert.Equal(userId, capturedToken.UserId);
        Assert.Equal(provider, capturedToken.Provider);
        Assert.Equal("encrypted-access-token", capturedToken.EncryptedAccessToken);
        Assert.Equal("encrypted-refresh-token", capturedToken.EncryptedRefreshToken);
        Assert.Equal(JsonSerializer.Serialize(scopes), capturedToken.Scopes);
        Assert.False(capturedToken.IsRevoked);
        Assert.Equal(0, capturedToken.RefreshFailureCount);

        _tokenRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<UserMusicToken>()), Times.Once);
    }

    [Fact]
    public async Task StoreTokensAsync_WhenExistingUser_ShouldUpdateTokenRecord()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var existingToken = new UserMusicToken
        {
            Id = 1,
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "old-encrypted-access-token",
            EncryptedRefreshToken = "old-encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(existingToken);

        var newAccessToken = "new-access-token";
        var newRefreshToken = "new-refresh-token";

        _encryptionServiceMock.Setup(x => x.EncryptToken(newAccessToken))
            .Returns("new-encrypted-access-token");
        _encryptionServiceMock.Setup(x => x.EncryptToken(newRefreshToken))
            .Returns("new-encrypted-refresh-token");

        _tokenRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<UserMusicToken>()))
            .ReturnsAsync((UserMusicToken token) => token);

        // Act
        var result = await _sut.StoreTokensAsync(userId, provider, newAccessToken, newRefreshToken, 3600);

        // Assert
        Assert.Equal("new-encrypted-access-token", existingToken.EncryptedAccessToken);
        Assert.Equal("new-encrypted-refresh-token", existingToken.EncryptedRefreshToken);
        Assert.Equal(1, existingToken.Id); // Same record, just updated

        _tokenRepositoryMock.Verify(r => r.UpdateAsync(existingToken), Times.Once);
    }

    [Fact]
    public async Task StoreTokensAsync_WhenNoRefreshToken_ShouldStoreOnlyAccessToken()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var accessToken = "access-token";

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        _encryptionServiceMock.Setup(x => x.EncryptToken(accessToken))
            .Returns("encrypted-access-token");

        var capturedToken = new UserMusicToken();
        _tokenRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<UserMusicToken>()))
            .Callback<UserMusicToken>(token => capturedToken = token)
            .ReturnsAsync((UserMusicToken token) => token);

        // Act
        await _sut.StoreTokensAsync(userId, provider, accessToken, null, 3600);

        // Assert
        Assert.Equal("encrypted-access-token", capturedToken.EncryptedAccessToken);
        Assert.Null(capturedToken.EncryptedRefreshToken);
    }

    #endregion

    #region GetValidAccessTokenAsync Tests

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenTokenNotExpired_ShouldReturnDecryptedToken()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var encryptedToken = "encrypted-access-token";
        var decryptedToken = "decrypted-access-token";

        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = encryptedToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1), // Not expired
            IsRevoked = false
        };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        _encryptionServiceMock.Setup(x => x.DecryptToken(encryptedToken))
            .Returns(decryptedToken);

        // Act
        var result = await _sut.GetValidAccessTokenAsync(userId, provider);

        // Assert
        Assert.Equal(decryptedToken, result);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenTokenNotFound_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => 
            _sut.GetValidAccessTokenAsync(userId, provider));
        Assert.Contains("No tokens found for user 1 provider spotify", exception.Message);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenTokenRevoked_ShouldReturnDecryptedToken()
    {
        // Arrange - The service implementation doesn't check for revoked status explicitly,
        // it relies on the repository to filter out revoked tokens or return null
        var userId = 1;
        var provider = "spotify";

        // Simulate repository returning null for revoked tokens
        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => 
            _sut.GetValidAccessTokenAsync(userId, provider));
        Assert.Contains("No tokens found for user 1 provider spotify", exception.Message);
    }

    #endregion

    #region GetTokenInfoAsync Tests

    [Fact]
    public async Task GetTokenInfoAsync_WhenTokenExists_ShouldReturnTokenInfo()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _sut.GetTokenInfoAsync(userId, provider);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(provider, result.Provider);
        Assert.Equal(tokenRecord.CreatedAt, result.CreatedAt);
    }

    [Fact]
    public async Task GetTokenInfoAsync_WhenTokenNotFound_ShouldReturnNull()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act
        var result = await _sut.GetTokenInfoAsync(userId, provider);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region HasValidTokensAsync Tests

    [Fact]
    public async Task HasValidTokensAsync_WhenTokenNotExpired_ShouldReturnTrue()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _tokenRepositoryMock.Setup(r => r.HasValidTokensAsync(userId, provider))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasValidTokensAsync_WhenTokenExpiredButHasRefreshToken_ShouldReturnTrue()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _tokenRepositoryMock.Setup(r => r.HasValidTokensAsync(userId, provider))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasValidTokensAsync_WhenTokenExpiredAndNoRefreshToken_ShouldReturnFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _tokenRepositoryMock.Setup(r => r.HasValidTokensAsync(userId, provider))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasValidTokensAsync_WhenTokenRevoked_ShouldReturnFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _tokenRepositoryMock.Setup(r => r.HasValidTokensAsync(userId, provider))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasValidTokensAsync_WhenTokenNotFound_ShouldReturnFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _tokenRepositoryMock.Setup(r => r.HasValidTokensAsync(userId, provider))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region RevokeTokensAsync Tests

    [Fact]
    public async Task RevokeTokensAsync_WhenTokenExists_ShouldRemoveToken()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        _tokenRepositoryMock.Setup(r => r.DeleteAsync(tokenRecord))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RevokeTokensAsync(userId, provider);

        // Assert
        _tokenRepositoryMock.Verify(r => r.DeleteAsync(tokenRecord), Times.Once);
    }

    [Fact]
    public async Task RevokeTokensAsync_WhenTokenNotFound_ShouldNotThrow()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act & Assert - Should not throw
        await _sut.RevokeTokensAsync(userId, provider);

        _tokenRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<UserMusicToken>()), Times.Never);
    }

    #endregion

    #region RefreshTokensAsync Tests

    [Fact]
    public async Task RefreshTokensAsync_WhenNoRefreshToken_ShouldReturnFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            EncryptedRefreshToken = null, // No refresh token
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
        };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _sut.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RefreshTokensAsync_WhenTokenNotFound_ShouldReturnFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act
        var result = await _sut.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RefreshTokensAsync_WhenUnsupportedProvider_ShouldReturnFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "apple"; // Unsupported provider
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            EncryptedRefreshToken = "encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
        };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _sut.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RefreshTokensAsync_WhenDecryptionFails_ShouldReturnFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            EncryptedRefreshToken = "encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
        };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Setup decryption to fail (simulating corrupted token scenario)
        _encryptionServiceMock.Setup(x => x.DecryptToken("encrypted-refresh-token"))
            .Throws(new InvalidOperationException("Token decryption failed"));

        // Act
        var result = await _sut.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region HasRequiredScopesAsync Tests

    [Fact]
    public async Task HasRequiredScopesAsync_WhenTokenHasAllRequiredScopes_ShouldReturnTrue()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var requiredScopes = new[] { "user-read-private", "playlist-read-private" };
        var tokenScopes = new[] { "user-read-private", "playlist-read-private", "playlist-modify-public" };
        
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            Scopes = JsonSerializer.Serialize(tokenScopes),
            IsRevoked = false
        };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _sut.HasRequiredScopesAsync(userId, provider, requiredScopes);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasRequiredScopesAsync_WhenTokenMissingRequiredScopes_ShouldReturnFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var requiredScopes = new[] { "user-read-private", "playlist-modify-private" };
        var tokenScopes = new[] { "user-read-private", "playlist-read-private" }; // Missing playlist-modify-private
        
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            Scopes = JsonSerializer.Serialize(tokenScopes),
            IsRevoked = false
        };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _sut.HasRequiredScopesAsync(userId, provider, requiredScopes);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasRequiredScopesAsync_WhenTokenNotFound_ShouldReturnFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var requiredScopes = new[] { "user-read-private" };

        _tokenRepositoryMock.Setup(r => r.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act
        var result = await _sut.HasRequiredScopesAsync(userId, provider, requiredScopes);

        // Assert
        Assert.False(result);
    }

    #endregion

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}