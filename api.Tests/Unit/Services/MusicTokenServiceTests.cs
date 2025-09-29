using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for MusicTokenService
/// Tests encrypted token storage, validation, refresh, and scope management
/// </summary>
public class MusicTokenServiceTests
{
    private readonly Mock<IUserMusicTokenRepository> _mockTokenRepository;
    private readonly Mock<ITokenEncryptionService> _mockEncryptionService;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<MusicTokenService>> _mockLogger;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly MusicTokenService _musicTokenService;

    public MusicTokenServiceTests()
    {
        _mockTokenRepository = new Mock<IUserMusicTokenRepository>();
        _mockEncryptionService = new Mock<ITokenEncryptionService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<MusicTokenService>>();
        _mockHttpClient = new Mock<HttpClient>();

        _musicTokenService = new MusicTokenService(
            _mockTokenRepository.Object,
            _mockEncryptionService.Object,
            _mockConfiguration.Object,
            _mockLogger.Object,
            _mockHttpClient.Object);
    }

    [Fact]
    public async Task StoreTokensAsync_WithNewToken_CreatesNewToken()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var accessToken = "access_token_123";
        var refreshToken = "refresh_token_123";
        var expiresInSeconds = 3600;
        var scopes = new[] { "playlist-read-private", "playlist-modify-public" };
        var metadata = new { userId = "spotify_user_123" };

        var encryptedAccessToken = "encrypted_access_token";
        var encryptedRefreshToken = "encrypted_refresh_token";

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        _mockEncryptionService.Setup(x => x.EncryptToken(accessToken))
            .Returns(encryptedAccessToken);
        _mockEncryptionService.Setup(x => x.EncryptToken(refreshToken))
            .Returns(encryptedRefreshToken);

        var createdToken = CreateTestToken(userId, provider);
        _mockTokenRepository.Setup(x => x.CreateAsync(It.IsAny<UserMusicToken>()))
            .ReturnsAsync(createdToken);

        // Act
        var result = await _musicTokenService.StoreTokensAsync(
            userId, provider, accessToken, refreshToken, expiresInSeconds, scopes, metadata);

        // Assert
        Assert.NotNull(result);

        _mockTokenRepository.Verify(x => x.CreateAsync(It.Is<UserMusicToken>(t =>
            t.UserId == userId &&
            t.Provider == provider &&
            t.EncryptedAccessToken == encryptedAccessToken &&
            t.EncryptedRefreshToken == encryptedRefreshToken &&
            t.Scopes != null &&
            t.ProviderMetadata != null
        )), Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Created new tokens")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StoreTokensAsync_WithExistingToken_UpdatesToken()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var accessToken = "new_access_token";
        var refreshToken = "new_refresh_token";
        var expiresInSeconds = 3600;

        var existingToken = CreateTestToken(userId, provider);
        var encryptedAccessToken = "encrypted_new_access_token";
        var encryptedRefreshToken = "encrypted_new_refresh_token";

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(existingToken);

        _mockEncryptionService.Setup(x => x.EncryptToken(accessToken))
            .Returns(encryptedAccessToken);
        _mockEncryptionService.Setup(x => x.EncryptToken(refreshToken))
            .Returns(encryptedRefreshToken);

        // Act
        var result = await _musicTokenService.StoreTokensAsync(
            userId, provider, accessToken, refreshToken, expiresInSeconds);

        // Assert
        Assert.NotNull(result);

        _mockTokenRepository.Verify(x => x.UpdateAsync(It.Is<UserMusicToken>(t =>
            t.EncryptedAccessToken == encryptedAccessToken &&
            t.EncryptedRefreshToken == encryptedRefreshToken
        )), Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Updated tokens")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StoreTokensAsync_WithNullRefreshToken_StoresNullEncryptedRefreshToken()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var accessToken = "access_token_123";
        var expiresInSeconds = 3600;

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        _mockEncryptionService.Setup(x => x.EncryptToken(accessToken))
            .Returns("encrypted_access_token");

        var createdToken = CreateTestToken(userId, provider);
        _mockTokenRepository.Setup(x => x.CreateAsync(It.IsAny<UserMusicToken>()))
            .ReturnsAsync(createdToken);

        // Act
        await _musicTokenService.StoreTokensAsync(
            userId, provider, accessToken, null, expiresInSeconds);

        // Assert
        _mockTokenRepository.Verify(x => x.CreateAsync(It.Is<UserMusicToken>(t =>
            t.EncryptedRefreshToken == null
        )), Times.Once);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WithValidToken_ReturnsDecryptedToken()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var tokenRecord = CreateTestToken(userId, provider);
        tokenRecord.ExpiresAt = DateTime.UtcNow.AddHours(1); // Valid token

        var decryptedToken = "decrypted_access_token";

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);
        _mockEncryptionService.Setup(x => x.DecryptToken(tokenRecord.EncryptedAccessToken))
            .Returns(decryptedToken);

        // Act
        var result = await _musicTokenService.GetValidAccessTokenAsync(userId, provider);

        // Assert
        Assert.Equal(decryptedToken, result);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WithNoToken_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _musicTokenService.GetValidAccessTokenAsync(userId, provider));

        Assert.Contains($"No tokens found for user {userId} provider {provider}", exception.Message);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WithExpiredTokenAndNoRefresh_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var tokenRecord = CreateTestToken(userId, provider);
        tokenRecord.ExpiresAt = DateTime.UtcNow.AddMinutes(-10); // Expired token
        tokenRecord.EncryptedRefreshToken = null; // No refresh token

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _musicTokenService.GetValidAccessTokenAsync(userId, provider));

        Assert.Contains("Token expired and refresh failed", exception.Message);
    }

    [Fact]
    public async Task GetTokenInfoAsync_ReturnsRepositoryResult()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var tokenRecord = CreateTestToken(userId, provider);

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _musicTokenService.GetTokenInfoAsync(userId, provider);

        // Assert
        Assert.Equal(tokenRecord, result);
        _mockTokenRepository.Verify(x => x.GetByUserAndProviderAsync(userId, provider), Times.Once);
    }

    [Fact]
    public async Task HasValidTokensAsync_ReturnsRepositoryResult()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _mockTokenRepository.Setup(x => x.HasValidTokensAsync(userId, provider))
            .ReturnsAsync(true);

        // Act
        var result = await _musicTokenService.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.True(result);
        _mockTokenRepository.Verify(x => x.HasValidTokensAsync(userId, provider), Times.Once);
    }

    [Fact]
    public async Task RefreshTokensAsync_WithNoToken_ReturnsFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act
        var result = await _musicTokenService.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No refresh token available")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshTokensAsync_WithNoRefreshToken_ReturnsFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var tokenRecord = CreateTestToken(userId, provider);
        tokenRecord.EncryptedRefreshToken = null;

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _musicTokenService.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RefreshTokensAsync_WithUnsupportedProvider_ReturnsFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "apple_music";
        var tokenRecord = CreateTestToken(userId, provider);

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _musicTokenService.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Refresh not implemented")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RevokeTokensAsync_WithExistingToken_DeletesToken()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var tokenRecord = CreateTestToken(userId, provider);

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        await _musicTokenService.RevokeTokensAsync(userId, provider);

        // Assert
        _mockTokenRepository.Verify(x => x.DeleteAsync(tokenRecord), Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Revoked tokens")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RevokeTokensAsync_WithNoToken_DoesNothing()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act
        await _musicTokenService.RevokeTokensAsync(userId, provider);

        // Assert
        _mockTokenRepository.Verify(x => x.DeleteAsync(It.IsAny<UserMusicToken>()), Times.Never);
    }

    [Fact]
    public async Task HasRequiredScopesAsync_WithAllScopesPresent_ReturnsTrue()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var grantedScopes = new[] { "playlist-read-private", "playlist-modify-public", "user-read-email" };
        var requiredScopes = new[] { "playlist-read-private", "playlist-modify-public" };

        var tokenRecord = CreateTestToken(userId, provider);
        tokenRecord.Scopes = JsonSerializer.Serialize(grantedScopes);

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _musicTokenService.HasRequiredScopesAsync(userId, provider, requiredScopes);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasRequiredScopesAsync_WithMissingScopes_ReturnsFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var grantedScopes = new[] { "playlist-read-private" };
        var requiredScopes = new[] { "playlist-read-private", "playlist-modify-public" };

        var tokenRecord = CreateTestToken(userId, provider);
        tokenRecord.Scopes = JsonSerializer.Serialize(grantedScopes);

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _musicTokenService.HasRequiredScopesAsync(userId, provider, requiredScopes);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasRequiredScopesAsync_WithNoScopes_ReturnsFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var requiredScopes = new[] { "playlist-read-private" };

        var tokenRecord = CreateTestToken(userId, provider);
        tokenRecord.Scopes = null;

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _musicTokenService.HasRequiredScopesAsync(userId, provider, requiredScopes);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasRequiredScopesAsync_WithInvalidScopesJson_ReturnsFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var requiredScopes = new[] { "playlist-read-private" };

        var tokenRecord = CreateTestToken(userId, provider);
        tokenRecord.Scopes = "invalid json";

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync(tokenRecord);

        // Act
        var result = await _musicTokenService.HasRequiredScopesAsync(userId, provider, requiredScopes);

        // Assert
        Assert.False(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to parse scopes")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HasRequiredScopesAsync_WithNoToken_ReturnsFalse()
    {
        // Arrange
        var userId = 1;
        var provider = "spotify";
        var requiredScopes = new[] { "playlist-read-private" };

        _mockTokenRepository.Setup(x => x.GetByUserAndProviderAsync(userId, provider))
            .ReturnsAsync((UserMusicToken?)null);

        // Act
        var result = await _musicTokenService.HasRequiredScopesAsync(userId, provider, requiredScopes);

        // Assert
        Assert.False(result);
    }

    private static UserMusicToken CreateTestToken(int userId, string provider)
    {
        return new UserMusicToken
        {
            Id = 1,
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted_access_token",
            EncryptedRefreshToken = "encrypted_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Scopes = JsonSerializer.Serialize(new[] { "playlist-read-private", "playlist-modify-public" }),
            ProviderMetadata = JsonSerializer.Serialize(new { userId = "spotify_user_123" }),
            RefreshFailureCount = 0,
            LastRefreshAt = null,
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        };
    }
}