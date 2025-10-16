using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Infrastructure.Repositories;

public class PlaylistSyncHistoryRepositoryTests : IDisposable
{
  private readonly RadioWashDbContext _context;
  private readonly PlaylistSyncHistoryRepository _repository;

  public PlaylistSyncHistoryRepositoryTests()
  {
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;

    _context = new RadioWashDbContext(options);
    _repository = new PlaylistSyncHistoryRepository(_context);
  }

  public void Dispose()
  {
    _context.Dispose();
  }

  [Fact]
  public async Task GetByIdAsync_WithExistingId_ShouldReturnHistory()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var history = CreateSyncHistory(1);
    _context.PlaylistSyncHistory.Add(history);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByIdAsync(history.Id);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(history.Id, result.Id);
    Assert.Equal(1, result.SyncConfigId);
  }

  [Fact]
  public async Task GetByIdAsync_WithNonExistentId_ShouldReturnNull()
  {
    // Act
    var result = await _repository.GetByIdAsync(999);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetByConfigIdAsync_WithDefaultLimit_ShouldReturnRecentEntries()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    await SeedSecondConfigAsync();

    var oldHistory = CreateSyncHistory(1);
    oldHistory.StartedAt = DateTime.UtcNow.AddHours(-2);

    var recentHistory1 = CreateSyncHistory(1);
    recentHistory1.StartedAt = DateTime.UtcNow.AddMinutes(-30);

    var recentHistory2 = CreateSyncHistory(1);
    recentHistory2.StartedAt = DateTime.UtcNow.AddMinutes(-15);

    var otherConfigHistory = CreateSyncHistory(2);
    otherConfigHistory.StartedAt = DateTime.UtcNow.AddMinutes(-10);

    _context.PlaylistSyncHistory.AddRange(oldHistory, recentHistory1, recentHistory2, otherConfigHistory);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByConfigIdAsync(1);

    // Assert
    Assert.Equal(3, result.Count()); // All entries for config 1
    Assert.All(result, h => Assert.Equal(1, h.SyncConfigId));
    // Should be ordered by StartedAt descending
    var orderedResult = result.ToList();
    Assert.True(orderedResult[0].StartedAt >= orderedResult[1].StartedAt);
    Assert.True(orderedResult[1].StartedAt >= orderedResult[2].StartedAt);
  }

  [Fact]
  public async Task GetByConfigIdAsync_WithCustomLimit_ShouldRespectLimit()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();

    for (int i = 0; i < 5; i++)
    {
      var history = CreateSyncHistory(1);
      history.StartedAt = DateTime.UtcNow.AddMinutes(-i * 10);
      _context.PlaylistSyncHistory.Add(history);
    }
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByConfigIdAsync(1, 3);

    // Assert
    Assert.Equal(3, result.Count());
    Assert.All(result, h => Assert.Equal(1, h.SyncConfigId));
  }

  [Fact]
  public async Task GetRecentHistoryAsync_ShouldReturnUserHistory()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    await SeedSecondUserWithConfigAsync();

    var user1History1 = CreateSyncHistory(1);
    var user1History2 = CreateSyncHistory(1);
    var user2History = CreateSyncHistory(2);

    _context.PlaylistSyncHistory.AddRange(user1History1, user1History2, user2History);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetRecentHistoryAsync(1);

    // Assert
    Assert.Equal(2, result.Count());
    Assert.All(result, h => Assert.Equal(1, h.SyncConfig!.UserId));
  }

  [Fact]
  public async Task GetRecentHistoryAsync_WithCustomLimit_ShouldRespectLimit()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();

    for (int i = 0; i < 5; i++)
    {
      var history = CreateSyncHistory(1);
      history.StartedAt = DateTime.UtcNow.AddMinutes(-i * 10);
      _context.PlaylistSyncHistory.Add(history);
    }
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetRecentHistoryAsync(1, 3);

    // Assert
    Assert.Equal(3, result.Count());
  }

  [Fact]
  public async Task CreateAsync_WithValidHistory_ShouldCreateAndReturnHistory()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var history = CreateSyncHistory(1);

    // Act
    var result = await _repository.CreateAsync(history);

    // Assert
    Assert.NotNull(result);
    Assert.True(result.Id > 0);
    Assert.Equal(1, result.SyncConfigId);

    var savedHistory = await _context.PlaylistSyncHistory.FindAsync(result.Id);
    Assert.NotNull(savedHistory);
    Assert.Equal(1, savedHistory.SyncConfigId);
  }

  [Fact]
  public async Task UpdateAsync_WithExistingHistory_ShouldUpdateHistory()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var history = CreateSyncHistory(1);
    _context.PlaylistSyncHistory.Add(history);
    await _context.SaveChangesAsync();

    history.Status = SyncStatus.Failed;
    history.ErrorMessage = "Test error";
    history.CompletedAt = DateTime.UtcNow;

    // Act
    var result = await _repository.UpdateAsync(history);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(SyncStatus.Failed, result.Status);
    Assert.Equal("Test error", result.ErrorMessage);
    Assert.NotNull(result.CompletedAt);

    var updatedHistory = await _context.PlaylistSyncHistory.FindAsync(history.Id);
    Assert.Equal(SyncStatus.Failed, updatedHistory!.Status);
    Assert.Equal("Test error", updatedHistory.ErrorMessage);
    Assert.NotNull(updatedHistory.CompletedAt);
  }

  [Fact]
  public async Task CompleteHistoryAsync_ShouldUpdateHistoryWithCompletionData()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var history = CreateSyncHistory(1);
    _context.PlaylistSyncHistory.Add(history);
    await _context.SaveChangesAsync();
    var historyId = history.Id;

    // Act
    await _repository.CompleteHistoryAsync(historyId, 5, 2, 10, 3000);

    // Assert
    var completedHistory = await _context.PlaylistSyncHistory.FindAsync(historyId);
    Assert.NotNull(completedHistory);
    Assert.Equal(SyncStatus.Completed, completedHistory.Status);
    Assert.Equal(5, completedHistory.TracksAdded);
    Assert.Equal(2, completedHistory.TracksRemoved);
    Assert.Equal(10, completedHistory.TracksUnchanged);
    Assert.Equal(3000, completedHistory.ExecutionTimeMs);
    Assert.NotNull(completedHistory.CompletedAt);
  }

  [Fact]
  public async Task FailHistoryAsync_ShouldUpdateHistoryWithFailureData()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var history = CreateSyncHistory(1);
    _context.PlaylistSyncHistory.Add(history);
    await _context.SaveChangesAsync();
    var historyId = history.Id;

    // Act
    await _repository.FailHistoryAsync(historyId, "Sync operation failed");

    // Assert
    var failedHistory = await _context.PlaylistSyncHistory.FindAsync(historyId);
    Assert.NotNull(failedHistory);
    Assert.Equal(SyncStatus.Failed, failedHistory.Status);
    Assert.Equal("Sync operation failed", failedHistory.ErrorMessage);
    Assert.NotNull(failedHistory.CompletedAt);
  }

  [Fact]
  public async Task SaveChangesAsync_ShouldPersistChanges()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var history = CreateSyncHistory(1);
    _context.PlaylistSyncHistory.Add(history);

    // Act
    await _repository.SaveChangesAsync();

    // Assert
    var savedHistory = await _context.PlaylistSyncHistory
        .FirstOrDefaultAsync(h => h.SyncConfigId == 1);
    Assert.NotNull(savedHistory);
  }

  private async Task SeedRequiredEntitiesAsync()
  {
    // Add a user
    var user = new User
    {
      Id = 1,
      SupabaseId = "test-uuid-1",
      DisplayName = "testuser",
      Email = "test@example.com",
      CreatedAt = DateTime.UtcNow
    };
    _context.Users.Add(user);

    // Add a job
    var job = new CleanPlaylistJob
    {
      Id = 1,
      UserId = 1,
      SourcePlaylistId = "source123",
      TargetPlaylistId = "target123",
      SourcePlaylistName = "Test Source",
      TargetPlaylistName = "Test Target",
      Status = JobStatus.Completed,
      CreatedAt = DateTime.UtcNow
    };
    _context.CleanPlaylistJobs.Add(job);

    // Add a sync config
    var config = new PlaylistSyncConfig
    {
      Id = 1,
      UserId = 1,
      OriginalJobId = 1,
      SourcePlaylistId = "source123",
      TargetPlaylistId = "target123",
      IsActive = true,
      SyncFrequency = SyncFrequency.Daily,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _context.PlaylistSyncConfigs.Add(config);

    await _context.SaveChangesAsync();
  }

  private async Task SeedSecondConfigAsync()
  {
    var config2 = new PlaylistSyncConfig
    {
      Id = 2,
      UserId = 1,
      OriginalJobId = 1,
      SourcePlaylistId = "source456",
      TargetPlaylistId = "target456",
      IsActive = true,
      SyncFrequency = SyncFrequency.Daily,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _context.PlaylistSyncConfigs.Add(config2);
    await _context.SaveChangesAsync();
  }

  private async Task SeedSecondUserWithConfigAsync()
  {
    var user2 = new User
    {
      Id = 2,
      SupabaseId = "test-uuid-2",
      DisplayName = "testuser2",
      Email = "test2@example.com",
      CreatedAt = DateTime.UtcNow
    };
    _context.Users.Add(user2);

    var job2 = new CleanPlaylistJob
    {
      Id = 2,
      UserId = 2,
      SourcePlaylistId = "source456",
      TargetPlaylistId = "target456",
      SourcePlaylistName = "Test Source 2",
      TargetPlaylistName = "Test Target 2",
      Status = JobStatus.Completed,
      CreatedAt = DateTime.UtcNow
    };
    _context.CleanPlaylistJobs.Add(job2);

    var config2 = new PlaylistSyncConfig
    {
      Id = 2,
      UserId = 2,
      OriginalJobId = 2,
      SourcePlaylistId = "source456",
      TargetPlaylistId = "target456",
      IsActive = true,
      SyncFrequency = SyncFrequency.Daily,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _context.PlaylistSyncConfigs.Add(config2);

    await _context.SaveChangesAsync();
  }

  private static PlaylistSyncHistory CreateSyncHistory(int configId)
  {
    return new PlaylistSyncHistory
    {
      SyncConfigId = configId,
      StartedAt = DateTime.UtcNow,
      Status = SyncStatus.Running,
      TracksAdded = 0,
      TracksRemoved = 0,
      TracksUnchanged = 0,
      ExecutionTimeMs = 0
    };
  }
}
