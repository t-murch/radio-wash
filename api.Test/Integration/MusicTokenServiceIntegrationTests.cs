using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using System.Net;
using System.Text;
using Xunit;

namespace RadioWash.Api.Test.Integration;

public class MusicTokenServiceIntegrationTests : IntegrationTestBase
{
    private readonly Mock<ITokenEncryptionService> _encryptionServiceMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<MusicTokenService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly MusicTokenService _sut;
    private readonly IUserMusicTokenRepository _tokenRepository;

    public MusicTokenServiceIntegrationTests()
    {
        _encryptionServiceMock = new Mock<ITokenEncryptionService>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<MusicTokenService>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        // Setup HttpClient with mocked handler
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);

        // Setup default configuration
        _configurationMock.Setup(c => c["Spotify:ClientId"]).Returns("fake-test-client-id");
        _configurationMock.Setup(c => c["Spotify:ClientSecret"]).Returns("fake-test-client-secret");

        // Get the repository from DI container
        _tokenRepository = Scope.ServiceProvider.GetRequiredService<IUserMusicTokenRepository>();

        _sut = new MusicTokenService(
            _tokenRepository,
            _encryptionServiceMock.Object,
            _configurationMock.Object,
            _loggerMock.Object,
            _httpClient);
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }

    #region StoreTokensAsync Tests

    [Fact]
    public async Task StoreTokensAsync_WhenNewUser_ShouldCreateNewTokenRecord()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var accessToken = "access-token";
        var refreshToken = "refresh-token";
        var expiresIn = 3600;
        var scopes = new[] { "user-read-private", "playlist-read-private" };
        var metadata = new { displayName = "Test User" };

        _encryptionServiceMock.Setup(x => x.EncryptToken(accessToken))
            .Returns("encrypted-access-token");
        _encryptionServiceMock.Setup(x => x.EncryptToken(refreshToken))
            .Returns("encrypted-refresh-token");

        // Act
        await _sut.StoreTokensAsync(userId, provider, accessToken, refreshToken, expiresIn, scopes, metadata);

        // Assert
        var storedToken = await DbContext!.UserMusicTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == provider);

        Assert.NotNull(storedToken);
        Assert.Equal("encrypted-access-token", storedToken.EncryptedAccessToken);
        Assert.Equal("encrypted-refresh-token", storedToken.EncryptedRefreshToken);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(scopes), storedToken.Scopes);
        Assert.True(storedToken.ExpiresAt > DateTime.UtcNow.AddSeconds(3500)); // Approximately 1 hour from now
        Assert.False(storedToken.IsRevoked);
        Assert.Equal(0, storedToken.RefreshFailureCount);
    }

    [Fact]
    public async Task StoreTokensAsync_WhenExistingUser_ShouldUpdateTokenRecord()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var existingToken = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "old-encrypted-access-token",
            EncryptedRefreshToken = "old-encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        await DbContext!.UserMusicTokens.AddAsync(existingToken);
        await DbContext!.SaveChangesAsync();

        var newAccessToken = "new-access-token";
        var newRefreshToken = "new-refresh-token";

        _encryptionServiceMock.Setup(x => x.EncryptToken(newAccessToken))
            .Returns("new-encrypted-access-token");
        _encryptionServiceMock.Setup(x => x.EncryptToken(newRefreshToken))
            .Returns("new-encrypted-refresh-token");

        // Act
        await _sut.StoreTokensAsync(userId, provider, newAccessToken, newRefreshToken, 3600);

        // Assert
        var updatedToken = await DbContext!.UserMusicTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == provider);

        Assert.NotNull(updatedToken);
        Assert.Equal("new-encrypted-access-token", updatedToken.EncryptedAccessToken);
        Assert.Equal("new-encrypted-refresh-token", updatedToken.EncryptedRefreshToken);
        Assert.Equal(existingToken.Id, updatedToken.Id); // Same record, just updated
    }

    [Fact]
    public async Task StoreTokensAsync_WhenNoRefreshToken_ShouldStoreOnlyAccessToken()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var accessToken = "access-token";

        _encryptionServiceMock.Setup(x => x.EncryptToken(accessToken))
            .Returns("encrypted-access-token");

        // Act
        await _sut.StoreTokensAsync(userId, provider, accessToken, null, 3600);

        // Assert
        var storedToken = await DbContext!.UserMusicTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == provider);

        Assert.NotNull(storedToken);
        Assert.Equal("encrypted-access-token", storedToken.EncryptedAccessToken);
        Assert.Null(storedToken.EncryptedRefreshToken);
    }

    #endregion

    #region GetValidAccessTokenAsync Tests

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenTokenNotExpired_ShouldReturnDecryptedToken()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
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

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

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
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => 
            _sut.GetValidAccessTokenAsync(userId, provider));
        Assert.Contains($"No tokens found for user {userId} provider spotify", exception.Message);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenTokenRevoked_ShouldThrowUnauthorizedException()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";

        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsRevoked = true
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => 
            _sut.GetValidAccessTokenAsync(userId, provider));
        Assert.Contains($"No tokens found for user {userId} provider spotify", exception.Message);
    }

    #endregion

    #region GetTokenInfoAsync Tests

    [Fact]
    public async Task GetTokenInfoAsync_WhenTokenExists_ShouldReturnTokenInfo()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

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
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";

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
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1), // Not expired
            IsRevoked = false
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

        // Act
        var result = await _sut.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasValidTokensAsync_WhenTokenExpiredButHasRefreshToken_ShouldReturnTrue()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            EncryptedRefreshToken = "encrypted-refresh-token", // Has refresh token
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10), // Expired
            IsRevoked = false
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

        // Act
        var result = await _sut.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasValidTokensAsync_WhenTokenExpiredAndNoRefreshToken_ShouldReturnFalse()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            EncryptedRefreshToken = null, // No refresh token
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10), // Expired
            IsRevoked = false
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

        // Act
        var result = await _sut.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasValidTokensAsync_WhenTokenRevoked_ShouldReturnFalse()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsRevoked = true // Revoked
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

        // Act
        var result = await _sut.HasValidTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasValidTokensAsync_WhenTokenNotFound_ShouldReturnFalse()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";

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
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

        // Act
        await _sut.RevokeTokensAsync(userId, provider);

        // Assert
        var remainingToken = await DbContext!.UserMusicTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == provider);
        Assert.Null(remainingToken);
    }

    [Fact]
    public async Task RevokeTokensAsync_WhenTokenNotFound_ShouldNotThrow()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";

        // Act & Assert - Should not throw
        await _sut.RevokeTokensAsync(userId, provider);
    }

    #endregion

    #region RefreshTokensAsync Tests

    [Fact]
    public async Task RefreshTokensAsync_WhenNoRefreshToken_ShouldReturnFalse()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            EncryptedRefreshToken = null, // No refresh token
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

        // Act
        var result = await _sut.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RefreshTokensAsync_WhenTokenNotFound_ShouldReturnFalse()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";

        // Act
        var result = await _sut.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RefreshTokensAsync_WhenUnsupportedProvider_ShouldReturnFalse()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "apple"; // Unsupported provider
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            EncryptedRefreshToken = "encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

        // Act
        var result = await _sut.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RefreshTokensAsync_WhenDecryptionFails_ShouldReturnFalse()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var tokenRecord = new UserMusicToken
        {
            UserId = userId,
            Provider = provider,
            EncryptedAccessToken = "encrypted-access-token",
            EncryptedRefreshToken = "encrypted-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
        };

        await DbContext!.UserMusicTokens.AddAsync(tokenRecord);
        await DbContext!.SaveChangesAsync();

        // Setup decryption to fail (simulating corrupted token scenario)
        _encryptionServiceMock.Setup(x => x.DecryptToken("encrypted-refresh-token"))
            .Throws(new InvalidOperationException("Token decryption failed"));

        // Act
        var result = await _sut.RefreshTokensAsync(userId, provider);

        // Assert
        Assert.False(result);
        
        // Verify the token was not marked as revoked since that would require a code change
        // This test documents the current behavior where decryption failures return false
    }

    #endregion

    #region Token Lifecycle Tests

    [Fact]
    public async Task TokenLifecycle_StoreRetrieveRevoke_ShouldWorkCorrectly()
    {
        // Arrange
        CleanupTestData();
        var testUser = SeedTestData();
        
        var userId = testUser.Id;
        var provider = "spotify";
        var accessToken = "test-access-token";
        var refreshToken = "test-refresh-token";

        _encryptionServiceMock.Setup(x => x.EncryptToken(accessToken))
            .Returns("encrypted-access-token");
        _encryptionServiceMock.Setup(x => x.EncryptToken(refreshToken))
            .Returns("encrypted-refresh-token");
        _encryptionServiceMock.Setup(x => x.DecryptToken("encrypted-access-token"))
            .Returns(accessToken);

        // Act & Assert - Store
        await _sut.StoreTokensAsync(userId, provider, accessToken, refreshToken, 3600);
        
        var hasTokens = await _sut.HasValidTokensAsync(userId, provider);
        Assert.True(hasTokens);

        // Act & Assert - Retrieve
        var retrievedToken = await _sut.GetValidAccessTokenAsync(userId, provider);
        Assert.Equal(accessToken, retrievedToken);

        // Act & Assert - Revoke
        await _sut.RevokeTokensAsync(userId, provider);
        
        var hasTokensAfterRevoke = await _sut.HasValidTokensAsync(userId, provider);
        Assert.False(hasTokensAfterRevoke);
        
        // After revoke, GetValidAccessTokenAsync should throw an exception since no tokens exist
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => 
            _sut.GetValidAccessTokenAsync(userId, provider));
    }

    #endregion
}