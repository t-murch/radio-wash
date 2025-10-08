using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Unit.Infrastructure;

namespace RadioWash.Api.Tests.Unit.Infrastructure.Repositories;

/// <summary>
/// Unit tests for UserMusicTokenRepository
/// Tests encrypted token storage, retrieval, validation, and lifecycle management
/// </summary>
public class UserMusicTokenRepositoryTests : RepositoryTestBase
{
  private readonly UserMusicTokenRepository _tokenRepository;

  public UserMusicTokenRepositoryTests()
  {
    _tokenRepository = new UserMusicTokenRepository(_context);
  }

  [Fact]
  public async Task GetByUserAndProviderAsync_WithValidToken_ReturnsToken()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    await SeedAsync(token);

    // Act
    var result = await _tokenRepository.GetByUserAndProviderAsync(user.Id, "spotify");

    // Assert
    Assert.NotNull(result);
    Assert.Equal(user.Id, result.UserId);
    Assert.Equal("spotify", result.Provider);
    Assert.Equal("encrypted_access_token", result.EncryptedAccessToken);
    Assert.Equal("encrypted_refresh_token", result.EncryptedRefreshToken);
    Assert.False(result.IsRevoked);
  }

  [Fact]
  public async Task GetByUserAndProviderAsync_WithRevokedToken_ReturnsNull()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    token.IsRevoked = true;
    await SeedAsync(token);

    // Act
    var result = await _tokenRepository.GetByUserAndProviderAsync(user.Id, "spotify");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetByUserAndProviderAsync_WithDifferentUser_ReturnsNull()
  {
    // Arrange
    var user1 = CreateTestUser(supabaseId: "sb_user1");
    var user2 = CreateTestUser(supabaseId: "sb_user2");
    await SeedAsync(user1, user2);

    var token = CreateTestMusicToken(user1.Id, "spotify");
    await SeedAsync(token);

    // Act
    var result = await _tokenRepository.GetByUserAndProviderAsync(user2.Id, "spotify");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetByUserAndProviderAsync_WithDifferentProvider_ReturnsNull()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    await SeedAsync(token);

    // Act
    var result = await _tokenRepository.GetByUserAndProviderAsync(user.Id, "google");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetByUserAndProviderAsync_WithNonExistentToken_ReturnsNull()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    // Act
    var result = await _tokenRepository.GetByUserAndProviderAsync(user.Id, "spotify");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task CreateAsync_WithValidToken_CreatesTokenSuccessfully()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(
        user.Id,
        "google",
        "encrypted_google_access",
        "encrypted_google_refresh");

    // Act
    var result = await _tokenRepository.CreateAsync(token);

    // Assert
    Assert.NotNull(result);
    Assert.True(result.Id > 0);
    Assert.Equal(user.Id, result.UserId);
    Assert.Equal("google", result.Provider);
    Assert.Equal("encrypted_google_access", result.EncryptedAccessToken);
    Assert.Equal("encrypted_google_refresh", result.EncryptedRefreshToken);

    // Verify it was saved to database
    var savedToken = await _context.UserMusicTokens.FindAsync(result.Id);
    Assert.NotNull(savedToken);
    Assert.Equal("google", savedToken.Provider);
  }

  [Fact]
  public async Task UpdateAsync_WithValidToken_UpdatesTokenAndTimestamp()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    await SeedAsync(token);

    var originalUpdatedAt = token.UpdatedAt;

    // Detach to simulate fresh load
    DetachAllEntities();

    var tokenToUpdate = await _context.UserMusicTokens.FindAsync(token.Id);
    Assert.NotNull(tokenToUpdate);

    tokenToUpdate.EncryptedAccessToken = "new_encrypted_access_token";
    tokenToUpdate.ExpiresAt = DateTime.UtcNow.AddHours(2);
    tokenToUpdate.RefreshFailureCount = 1;

    // Act
    var result = await _tokenRepository.UpdateAsync(tokenToUpdate);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("new_encrypted_access_token", result.EncryptedAccessToken);
    Assert.Equal(1, result.RefreshFailureCount);
    Assert.True(result.UpdatedAt > originalUpdatedAt);

    // Verify it was updated in database
    DetachAllEntities();
    var updatedToken = await _context.UserMusicTokens.FindAsync(token.Id);
    Assert.NotNull(updatedToken);
    Assert.Equal("new_encrypted_access_token", updatedToken.EncryptedAccessToken);
    Assert.Equal(1, updatedToken.RefreshFailureCount);
  }

  [Fact]
  public async Task DeleteAsync_WithValidToken_DeletesToken()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    await SeedAsync(token);

    var tokenId = token.Id;

    // Act
    await _tokenRepository.DeleteAsync(token);

    // Assert
    var deletedToken = await _context.UserMusicTokens.FindAsync(tokenId);
    Assert.Null(deletedToken);
  }

  [Fact]
  public async Task HasValidTokensAsync_WithValidNonExpiredToken_ReturnsTrue()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    token.ExpiresAt = DateTime.UtcNow.AddHours(1); // Valid for 1 hour
    await SeedAsync(token);

    // Act
    var result = await _tokenRepository.HasValidTokensAsync(user.Id, "spotify");

    // Assert
    Assert.True(result);
  }

  [Fact]
  public async Task HasValidTokensAsync_WithExpiredTokenButRefreshToken_ReturnsTrue()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    token.ExpiresAt = DateTime.UtcNow.AddMinutes(-30); // Expired 30 minutes ago
    token.EncryptedRefreshToken = "encrypted_refresh_token"; // But has refresh token
    await SeedAsync(token);

    // Act
    var result = await _tokenRepository.HasValidTokensAsync(user.Id, "spotify");

    // Assert
    Assert.True(result);
  }

  [Fact]
  public async Task HasValidTokensAsync_WithExpiredTokenAndNoRefreshToken_ReturnsFalse()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    token.ExpiresAt = DateTime.UtcNow.AddMinutes(-30); // Expired 30 minutes ago
    token.EncryptedRefreshToken = null; // No refresh token
    await SeedAsync(token);

    // Act
    var result = await _tokenRepository.HasValidTokensAsync(user.Id, "spotify");

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task HasValidTokensAsync_WithExpiredTokenAndEmptyRefreshToken_ReturnsFalse()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    token.ExpiresAt = DateTime.UtcNow.AddMinutes(-30); // Expired 30 minutes ago
    token.EncryptedRefreshToken = ""; // Empty refresh token
    await SeedAsync(token);

    // Act
    var result = await _tokenRepository.HasValidTokensAsync(user.Id, "spotify");

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task HasValidTokensAsync_WithRevokedToken_ReturnsFalse()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    token.ExpiresAt = DateTime.UtcNow.AddHours(1); // Valid expiration
    token.IsRevoked = true; // But revoked
    await SeedAsync(token);

    // Act
    var result = await _tokenRepository.HasValidTokensAsync(user.Id, "spotify");

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task HasValidTokensAsync_WithNoToken_ReturnsFalse()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    // Act
    var result = await _tokenRepository.HasValidTokensAsync(user.Id, "spotify");

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task SaveChangesAsync_WithPendingChanges_SavesChanges()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    _context.UserMusicTokens.Add(token);

    // Act
    await _tokenRepository.SaveChangesAsync();

    // Assert
    Assert.True(token.Id > 0);

    // Verify it was saved
    var savedToken = await _context.UserMusicTokens.FindAsync(token.Id);
    Assert.NotNull(savedToken);
    Assert.Equal(token.Provider, savedToken.Provider);
  }

  [Fact]
  public async Task GetByUserAndProviderAsync_WithMultipleTokensForUser_ReturnsCorrectToken()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var spotifyToken = CreateTestMusicToken(user.Id, "spotify");
    var googleToken = CreateTestMusicToken(user.Id, "google");
    await SeedAsync(spotifyToken, googleToken);

    // Act
    var spotifyResult = await _tokenRepository.GetByUserAndProviderAsync(user.Id, "spotify");
    var googleResult = await _tokenRepository.GetByUserAndProviderAsync(user.Id, "google");

    // Assert
    Assert.NotNull(spotifyResult);
    Assert.NotNull(googleResult);
    Assert.Equal("spotify", spotifyResult.Provider);
    Assert.Equal("google", googleResult.Provider);
    Assert.NotEqual(spotifyResult.Id, googleResult.Id);
  }

  [Fact]
  public async Task UpdateAsync_MarkRefreshSuccess_UpdatesTimestampAndCounters()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    token.RefreshFailureCount = 3;
    await SeedAsync(token);

    // Detach to simulate fresh load
    DetachAllEntities();

    var tokenToUpdate = await _context.UserMusicTokens.FindAsync(token.Id);
    Assert.NotNull(tokenToUpdate);

    tokenToUpdate.MarkRefreshSuccess();

    // Act
    var result = await _tokenRepository.UpdateAsync(tokenToUpdate);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(0, result.RefreshFailureCount);
    Assert.NotNull(result.LastRefreshAt);
    Assert.True(result.LastRefreshAt > DateTime.UtcNow.AddMinutes(-1));
  }

  [Fact]
  public async Task UpdateAsync_MarkRefreshFailure_IncrementsFailureCount()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var token = CreateTestMusicToken(user.Id, "spotify");
    token.RefreshFailureCount = 2;
    await SeedAsync(token);

    // Detach to simulate fresh load
    DetachAllEntities();

    var tokenToUpdate = await _context.UserMusicTokens.FindAsync(token.Id);
    Assert.NotNull(tokenToUpdate);

    tokenToUpdate.MarkRefreshFailure();

    // Act
    var result = await _tokenRepository.UpdateAsync(tokenToUpdate);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(3, result.RefreshFailureCount);
  }
}
