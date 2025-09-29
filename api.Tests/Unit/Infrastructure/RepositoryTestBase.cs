using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Tests.Unit.Infrastructure;

/// <summary>
/// Base class for repository unit tests providing common infrastructure
/// Uses EF Core InMemory database for fast, isolated testing
/// </summary>
public abstract class RepositoryTestBase : IDisposable
{
  protected readonly RadioWashDbContext _context;

  protected RepositoryTestBase()
  {
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;

    _context = new RadioWashDbContext(options);

    // Ensure the database is created
    _context.Database.EnsureCreated();
  }

  /// <summary>
  /// Creates a test user with default values
  /// </summary>
  protected User CreateTestUser(
      string supabaseId = "sb_test_123",
      string displayName = "Test User",
      string email = "test@example.com",
      string? primaryProvider = "email")
  {
    return new User
    {
      SupabaseId = supabaseId,
      DisplayName = displayName,
      Email = email,
      PrimaryProvider = primaryProvider,
      CreatedAt = DateTime.UtcNow.AddDays(-1),
      UpdatedAt = DateTime.UtcNow
    };
  }

  /// <summary>
  /// Creates a test user provider data entry
  /// </summary>
  protected UserProviderData CreateTestUserProviderData(
      int userId,
      string provider = "spotify",
      string providerId = "spotify_123",
      string? metadata = null)
  {
    return new UserProviderData
    {
      UserId = userId,
      Provider = provider,
      ProviderId = providerId,
      ProviderMetadata = metadata,
      CreatedAt = DateTime.UtcNow.AddDays(-1),
      UpdatedAt = DateTime.UtcNow
    };
  }

  /// <summary>
  /// Creates a test music token
  /// </summary>
  protected UserMusicToken CreateTestMusicToken(
      int userId,
      string provider = "spotify",
      string encryptedAccessToken = "encrypted_access_token",
      string? encryptedRefreshToken = "encrypted_refresh_token")
  {
    return new UserMusicToken
    {
      UserId = userId,
      Provider = provider,
      EncryptedAccessToken = encryptedAccessToken,
      EncryptedRefreshToken = encryptedRefreshToken,
      ExpiresAt = DateTime.UtcNow.AddHours(1),
      Scopes = "[\"playlist-read-private\", \"playlist-modify-public\"]",
      ProviderMetadata = "{\"userId\": \"spotify_user_123\"}",
      RefreshFailureCount = 0,
      LastRefreshAt = null,
      IsRevoked = false,
      CreatedAt = DateTime.UtcNow.AddDays(-1),
      UpdatedAt = DateTime.UtcNow
    };
  }

  /// <summary>
  /// Creates a test clean playlist job
  /// </summary>
  protected CleanPlaylistJob CreateTestCleanPlaylistJob(
      int userId,
      string sourcePlaylistId = "source_playlist_123",
      string targetPlaylistName = "Clean Playlist",
      string sourcePlaylistName = "Original Playlist")
  {
    return new CleanPlaylistJob
    {
      UserId = userId,
      SourcePlaylistId = sourcePlaylistId,
      SourcePlaylistName = sourcePlaylistName,
      TargetPlaylistName = targetPlaylistName,
      Status = "Pending",
      CreatedAt = DateTime.UtcNow.AddMinutes(-30),
      UpdatedAt = DateTime.UtcNow.AddMinutes(-30),
      ProcessedTracks = 0,
      TotalTracks = 0,
      MatchedTracks = 0
    };
  }

  /// <summary>
  /// Creates a test track mapping
  /// </summary>
  protected TrackMapping CreateTestTrackMapping(
      int jobId,
      string sourceTrackId = "source_track_123",
      string? targetTrackId = "target_track_123",
      bool isExplicit = true,
      bool hasCleanMatch = true,
      string sourceArtistName = "Test Artist",
      string? targetArtistName = null)
  {
    return new TrackMapping
    {
      JobId = jobId,
      SourceTrackId = sourceTrackId,
      SourceTrackName = "Test Song",
      SourceArtistName = sourceArtistName,
      TargetTrackId = targetTrackId,
      TargetTrackName = hasCleanMatch ? "Test Song (Clean)" : null,
      TargetArtistName = targetArtistName ?? (hasCleanMatch ? sourceArtistName : null),
      IsExplicit = isExplicit,
      HasCleanMatch = hasCleanMatch,
      CreatedAt = DateTime.UtcNow.AddMinutes(-15)
    };
  }

  /// <summary>
  /// Adds entities to the context and saves changes
  /// </summary>
  protected async Task SeedAsync(params object[] entities)
  {
    foreach (var entity in entities)
    {
      _context.Add(entity);
    }
    await _context.SaveChangesAsync();
  }

  /// <summary>
  /// Clears all data from the context
  /// </summary>
  protected async Task ClearDataAsync()
  {
    _context.TrackMappings.RemoveRange(_context.TrackMappings);
    _context.CleanPlaylistJobs.RemoveRange(_context.CleanPlaylistJobs);
    _context.UserMusicTokens.RemoveRange(_context.UserMusicTokens);
    _context.UserProviderData.RemoveRange(_context.UserProviderData);
    _context.Users.RemoveRange(_context.Users);
    await _context.SaveChangesAsync();
  }

  /// <summary>
  /// Detaches all entities from change tracking
  /// </summary>
  protected void DetachAllEntities()
  {
    var trackedEntities = _context.ChangeTracker.Entries().ToList();
    foreach (var entity in trackedEntities)
    {
      entity.State = EntityState.Detached;
    }
  }

  public virtual void Dispose()
  {
    _context.Dispose();
  }
}
