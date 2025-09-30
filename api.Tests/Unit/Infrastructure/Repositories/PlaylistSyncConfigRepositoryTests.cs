using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Infrastructure.Repositories;

public class PlaylistSyncConfigRepositoryTests : IDisposable
{
  private readonly RadioWashDbContext _context;
  private readonly PlaylistSyncConfigRepository _repository;

  public PlaylistSyncConfigRepositoryTests()
  {
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;

    _context = new RadioWashDbContext(options);
    _repository = new PlaylistSyncConfigRepository(_context);
  }

  public void Dispose()
  {
    _context.Dispose();
  }

  [Fact]
  public async Task GetByIdAsync_WithExistingId_ShouldReturnConfig()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var config = CreateSyncConfig(1);
    _context.PlaylistSyncConfigs.Add(config);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByIdAsync(config.Id);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(config.Id, result.Id);
    Assert.Equal(1, result.UserId);
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
  public async Task GetByUserIdAsync_ShouldReturnUserConfigs()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    await SeedSecondUserAsync();

    var config1 = CreateSyncConfig(1);
    var config2 = CreateSyncConfig(1);
    var config3 = CreateSyncConfig(2);

    _context.PlaylistSyncConfigs.AddRange(config1, config2, config3);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByUserIdAsync(1);

    // Assert
    Assert.Equal(2, result.Count());
    Assert.All(result, c => Assert.Equal(1, c.UserId));
  }

  [Fact]
  public async Task GetByJobIdAsync_WithExistingJobId_ShouldReturnConfig()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var config = CreateSyncConfig(1, 1); // Use jobId 1 which matches the seeded job
    _context.PlaylistSyncConfigs.Add(config);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByJobIdAsync(1);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(1, result.OriginalJobId);
    Assert.Equal(1, result.UserId);
  }

  [Fact]
  public async Task GetByJobIdAsync_WithNonExistentJobId_ShouldReturnNull()
  {
    // Act
    var result = await _repository.GetByJobIdAsync(999);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetActiveConfigsAsync_ShouldReturnOnlyActiveConfigs()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    await SeedSecondUserAsync();

    var activeConfig1 = CreateSyncConfig(1, isActive: true);
    var activeConfig2 = CreateSyncConfig(2, isActive: true);
    var inactiveConfig = CreateSyncConfig(1, isActive: false);

    _context.PlaylistSyncConfigs.AddRange(activeConfig1, activeConfig2, inactiveConfig);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetActiveConfigsAsync();

    // Assert
    Assert.Equal(2, result.Count());
    Assert.All(result, c => Assert.True(c.IsActive));
  }

  [Fact]
  public async Task GetDueForSyncAsync_ShouldReturnDueConfigs()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    await SeedSecondUserAsync();

    var pastDue = DateTime.UtcNow.AddMinutes(-10);
    var future = DateTime.UtcNow.AddHours(1);

    var readyConfig1 = CreateSyncConfig(1, isActive: true);
    readyConfig1.NextScheduledSync = pastDue;

    var readyConfig2 = CreateSyncConfig(2, isActive: true);
    readyConfig2.NextScheduledSync = pastDue;

    var futureConfig = CreateSyncConfig(1, isActive: true);
    futureConfig.NextScheduledSync = future;

    var inactiveConfig = CreateSyncConfig(1, isActive: false);
    inactiveConfig.NextScheduledSync = pastDue;

    _context.PlaylistSyncConfigs.AddRange(readyConfig1, readyConfig2, futureConfig, inactiveConfig);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetDueForSyncAsync(DateTime.UtcNow);

    // Assert
    Assert.Equal(2, result.Count());
    Assert.All(result, c => Assert.True(c.IsActive));
    Assert.All(result, c => Assert.True(c.NextScheduledSync <= DateTime.UtcNow));
  }

  [Fact]
  public async Task CreateAsync_WithValidConfig_ShouldCreateAndReturnConfig()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var config = CreateSyncConfig(1);

    // Act
    var result = await _repository.CreateAsync(config);

    // Assert
    Assert.NotNull(result);
    Assert.True(result.Id > 0);
    Assert.Equal(1, result.UserId);

    var savedConfig = await _context.PlaylistSyncConfigs.FindAsync(result.Id);
    Assert.NotNull(savedConfig);
    Assert.Equal(1, savedConfig.UserId);
  }

  [Fact]
  public async Task UpdateAsync_WithExistingConfig_ShouldUpdateConfig()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var config = CreateSyncConfig(1);
    _context.PlaylistSyncConfigs.Add(config);
    await _context.SaveChangesAsync();

    config.SyncFrequency = SyncFrequency.Weekly;
    config.IsActive = false;

    // Act
    var result = await _repository.UpdateAsync(config);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(SyncFrequency.Weekly, result.SyncFrequency);
    Assert.False(result.IsActive);

    var updatedConfig = await _context.PlaylistSyncConfigs.FindAsync(config.Id);
    Assert.Equal(SyncFrequency.Weekly, updatedConfig!.SyncFrequency);
    Assert.False(updatedConfig.IsActive);
  }

  [Fact]
  public async Task DisableConfigAsync_ShouldSetConfigToInactive()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var config = CreateSyncConfig(1, isActive: true);
    _context.PlaylistSyncConfigs.Add(config);
    await _context.SaveChangesAsync();
    var configId = config.Id;

    // Act
    await _repository.DisableConfigAsync(configId);

    // Assert
    var disabledConfig = await _context.PlaylistSyncConfigs.FindAsync(configId);
    Assert.NotNull(disabledConfig);
    Assert.False(disabledConfig.IsActive);
  }

  [Fact]
  public async Task UpdateLastSyncAsync_ShouldUpdateSyncFields()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var config = CreateSyncConfig(1);
    _context.PlaylistSyncConfigs.Add(config);
    await _context.SaveChangesAsync();
    var configId = config.Id;
    var syncTime = DateTime.UtcNow;

    // Act
    await _repository.UpdateLastSyncAsync(configId, syncTime, SyncStatus.Completed, null);

    // Assert
    var updatedConfig = await _context.PlaylistSyncConfigs.FindAsync(configId);
    Assert.NotNull(updatedConfig);
    Assert.Equal(syncTime.ToString("yyyy-MM-dd HH:mm:ss"), updatedConfig.LastSyncedAt?.ToString("yyyy-MM-dd HH:mm:ss"));
    Assert.Equal(SyncStatus.Completed, updatedConfig.LastSyncStatus);
    Assert.Null(updatedConfig.LastSyncError);
  }

  [Fact]
  public async Task UpdateNextScheduledSyncAsync_ShouldUpdateScheduledTime()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var config = CreateSyncConfig(1);
    _context.PlaylistSyncConfigs.Add(config);
    await _context.SaveChangesAsync();
    var configId = config.Id;
    var nextSync = DateTime.UtcNow.AddDays(1);

    // Act
    await _repository.UpdateNextScheduledSyncAsync(configId, nextSync);

    // Assert
    var updatedConfig = await _context.PlaylistSyncConfigs.FindAsync(configId);
    Assert.NotNull(updatedConfig);
    Assert.Equal(nextSync.ToString("yyyy-MM-dd HH:mm:ss"), updatedConfig.NextScheduledSync?.ToString("yyyy-MM-dd HH:mm:ss"));
  }

  [Fact]
  public async Task SaveChangesAsync_ShouldPersistChanges()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var config = CreateSyncConfig(1);
    _context.PlaylistSyncConfigs.Add(config);

    // Act
    await _repository.SaveChangesAsync();

    // Assert
    var savedConfig = await _context.PlaylistSyncConfigs
        .FirstOrDefaultAsync(c => c.UserId == 1);
    Assert.NotNull(savedConfig);
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

    await _context.SaveChangesAsync();
  }

  private async Task SeedSecondUserAsync()
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

    await _context.SaveChangesAsync();
  }

  private static PlaylistSyncConfig CreateSyncConfig(int userId, int jobId = 1, bool isActive = true)
  {
    return new PlaylistSyncConfig
    {
      UserId = userId,
      OriginalJobId = jobId,
      SourcePlaylistId = "source123",
      TargetPlaylistId = "target123",
      IsActive = isActive,
      SyncFrequency = SyncFrequency.Daily,
      NextScheduledSync = DateTime.UtcNow.AddHours(1),
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }
}
