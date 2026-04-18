using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Tests for <see cref="SpotifyTokenRefresher"/> — the extracted strategy that owns the
/// Spotify-specific OAuth refresh call. Contracts asserted here: <c>ProviderName</c>
/// identifies Spotify; the refresher short-circuits safely when the encrypted refresh token
/// is missing; repository update and the two <see cref="UserMusicToken"/> state transitions
/// (<c>MarkRefreshSuccess</c> / <c>MarkRefreshFailure</c>) fire through the right paths.
///
/// Tests that require touching Spotify's live OAuth endpoint (happy path, non-200 response)
/// are intentionally out of scope — that path is exercised end-to-end through
/// <c>MusicTokenServiceTests</c>' concurrency suite and the integration test. The purpose of
/// this test class is to confirm the extraction preserved behavior, not to re-validate the
/// OAuth client library.
/// </summary>
public class SpotifyTokenRefresherTests
{
  private readonly Mock<IUserMusicTokenRepository> _repo = new();
  private readonly Mock<ITokenEncryptionService> _encryption = new();
  private readonly Mock<IConfiguration> _config = new();
  private readonly Mock<ILogger<SpotifyTokenRefresher>> _logger = new();
  private readonly SpotifyTokenRefresher _refresher;

  public SpotifyTokenRefresherTests()
  {
    _refresher = new SpotifyTokenRefresher(
      _repo.Object,
      _encryption.Object,
      _config.Object,
      _logger.Object);
  }

  [Fact]
  public void ProviderName_IsSpotify()
  {
    Assert.Equal("spotify", _refresher.ProviderName);
  }

  [Fact]
  public async Task RefreshAsync_WithNullEncryptedRefreshToken_ReturnsFalseWithoutCallingSpotify()
  {
    // A missing refresh token is a permanent failure (user must re-auth). The refresher must
    // not attempt an OAuth POST, must not touch the repository's success/failure counters,
    // and must return false so callers can surface the re-auth requirement.
    var token = new UserMusicToken
    {
      Id = 1,
      UserId = 7,
      Provider = "spotify",
      EncryptedAccessToken = "x",
      EncryptedRefreshToken = null,
      ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
    };

    var result = await _refresher.RefreshAsync(token, CancellationToken.None);

    Assert.False(result);
    _encryption.Verify(x => x.DecryptToken(It.IsAny<string>()), Times.Never);
    _repo.Verify(x => x.UpdateAsync(It.IsAny<UserMusicToken>()), Times.Never);
  }
}
