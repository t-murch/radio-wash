using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class PlaylistSyncServiceTests
{
  private readonly Mock<IUnitOfWork> _mockUnitOfWork;
  private readonly Mock<ISpotifyService> _mockSpotifyService;
  private readonly Mock<IPlaylistDeltaCalculator> _mockDeltaCalculator;
  private readonly Mock<ISubscriptionService> _mockSubscriptionService;
  private readonly Mock<ISyncTimeCalculator> _mockSyncTimeCalculator;
  private readonly Mock<ILogger<PlaylistSyncService>> _mockLogger;
  private readonly PlaylistSyncService _syncService;

  public PlaylistSyncServiceTests()
  {
    _mockUnitOfWork = new Mock<IUnitOfWork>();
    _mockSpotifyService = new Mock<ISpotifyService>();
    _mockDeltaCalculator = new Mock<IPlaylistDeltaCalculator>();
    _mockSubscriptionService = new Mock<ISubscriptionService>();
    _mockSyncTimeCalculator = new Mock<ISyncTimeCalculator>();
    _mockLogger = new Mock<ILogger<PlaylistSyncService>>();

    _syncService = new PlaylistSyncService(
        _mockUnitOfWork.Object,
        _mockSpotifyService.Object,
        _mockDeltaCalculator.Object,
        _mockSubscriptionService.Object,
        _mockSyncTimeCalculator.Object,
        _mockLogger.Object
    );

    // Setup default mock behaviors
    SetupDefaultMocks();
  }

  private void SetupDefaultMocks()
  {
    _mockUnitOfWork.Setup(x => x.SyncHistory.CreateAsync(It.IsAny<PlaylistSyncHistory>()))
        .ReturnsAsync((PlaylistSyncHistory h) => { h.Id = 1; return h; });

    _mockUnitOfWork.Setup(x => x.TrackMappings.GetByJobIdAsync(It.IsAny<int>()))
        .ReturnsAsync(new List<TrackMapping>());

    _mockUnitOfWork.Setup(x => x.SaveChangesAsync())
        .ReturnsAsync(1);

    _mockUnitOfWork.Setup(x => x.SyncHistory.CompleteHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
        .Returns(Task.CompletedTask);

    _mockUnitOfWork.Setup(x => x.SyncHistory.FailHistoryAsync(It.IsAny<int>(), It.IsAny<string>()))
        .Returns(Task.CompletedTask);

    _mockUnitOfWork.Setup(x => x.SyncConfigs.UpdateLastSyncAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>()))
        .Returns(Task.CompletedTask);

    _mockUnitOfWork.Setup(x => x.SyncConfigs.UpdateNextScheduledSyncAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
        .Returns(Task.CompletedTask);

    _mockUnitOfWork.Setup(x => x.SyncConfigs.DisableConfigAsync(It.IsAny<int>()))
        .Returns(Task.CompletedTask);

    _mockUnitOfWork.Setup(x => x.TrackMappings.AddAsync(It.IsAny<TrackMapping>()))
        .Returns(Task.CompletedTask);
  }

  [Fact]
  public async Task SyncPlaylistAsync_WithInactiveSubscription_ShouldFailAndDisableConfig()
  {
    // Arrange
    var config = CreateSyncConfig();
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id))
        .ReturnsAsync(config);
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId))
        .ReturnsAsync(false);

    // Act
    var result = await _syncService.SyncPlaylistAsync(config.Id);

    // Assert
    Assert.False(result.Success);
    Assert.Contains("subscription", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

    _mockUnitOfWork.Verify(x => x.SyncConfigs.DisableConfigAsync(config.Id), Times.Once);
  }

  [Fact]
  public async Task SyncPlaylistAsync_WithActiveSubscription_ShouldPerformSync()
  {
    // Arrange
    var config = CreateSyncConfig();
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id))
        .ReturnsAsync(config);
    var sourceTracks = new List<SpotifyTrack> { CreateSpotifyTrack("1", "Track 1") };
    var targetTracks = new List<SpotifyTrack> { CreateSpotifyTrack("clean-1", "Clean Track 1") };
    var mappings = new List<TrackMapping> { CreateTrackMapping("1", "clean-1") };
    var delta = new PlaylistDelta
    {
      TracksToAdd = new List<string>(),
      TracksToRemove = new List<string>(),
      NewTracks = new List<SpotifyTrack>(),
      DesiredTrackOrder = new List<string> { "clean-1" }
    };

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId))
        .ReturnsAsync(true);
    _mockSpotifyService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.SourcePlaylistId))
        .ReturnsAsync(sourceTracks);
    _mockSpotifyService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.TargetPlaylistId))
        .ReturnsAsync(targetTracks);
    _mockUnitOfWork.Setup(x => x.TrackMappings.GetByJobIdAsync(config.OriginalJobId))
        .ReturnsAsync(mappings);
    _mockDeltaCalculator.Setup(x => x.CalculateDeltaAsync(
        It.IsAny<List<SpotifyTrack>>(),
        It.IsAny<List<SpotifyTrack>>(),
        It.IsAny<List<TrackMapping>>()))
        .ReturnsAsync(delta);
    _mockSyncTimeCalculator.Setup(x => x.CalculateNextSyncTime(It.IsAny<string>(), It.IsAny<DateTime?>()))
        .Returns(DateTime.UtcNow.AddDays(1));

    // Act
    var result = await _syncService.SyncPlaylistAsync(config.Id);

    // Assert
    Assert.True(result.Success);
    Assert.Equal(0, result.TracksAdded);
    Assert.Equal(0, result.TracksRemoved);

    _mockUnitOfWork.Verify(x => x.SyncHistory.CompleteHistoryAsync(
        It.IsAny<int>(),
        It.IsAny<int>(),
        It.IsAny<int>(),
        It.IsAny<int>(),
        It.IsAny<int>()), Times.Once);
  }

  [Fact]
  public async Task SyncPlaylistAsync_WithNewTracks_ShouldProcessAndAddThem()
  {
    // Arrange
    var config = CreateSyncConfig();
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id))
        .ReturnsAsync(config);
    var newTrack = CreateSpotifyTrack("2", "New Track");
    var cleanTrack = CreateSpotifyTrack("clean-2", "Clean New Track");
    var sourceTracks = new List<SpotifyTrack> { newTrack };
    var targetTracks = new List<SpotifyTrack>();
    var mappings = new List<TrackMapping>();
    var delta = new PlaylistDelta
    {
      TracksToAdd = new List<string>(),
      TracksToRemove = new List<string>(),
      NewTracks = new List<SpotifyTrack> { newTrack },
      DesiredTrackOrder = new List<string>()
    };

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId))
        .ReturnsAsync(true);
    _mockSpotifyService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.SourcePlaylistId))
        .ReturnsAsync(sourceTracks);
    _mockSpotifyService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.TargetPlaylistId))
        .ReturnsAsync(targetTracks);
    _mockUnitOfWork.Setup(x => x.TrackMappings.GetByJobIdAsync(config.OriginalJobId))
        .ReturnsAsync(mappings);
    _mockDeltaCalculator.Setup(x => x.CalculateDeltaAsync(
        It.IsAny<List<SpotifyTrack>>(),
        It.IsAny<List<SpotifyTrack>>(),
        It.IsAny<List<TrackMapping>>()))
        .ReturnsAsync(delta);
    _mockSpotifyService.Setup(x => x.FindCleanVersionAsync(config.UserId, newTrack))
        .ReturnsAsync(cleanTrack);
    _mockSyncTimeCalculator.Setup(x => x.CalculateNextSyncTime(It.IsAny<string>(), It.IsAny<DateTime?>()))
        .Returns(DateTime.UtcNow.AddDays(1));

    // Act
    var result = await _syncService.SyncPlaylistAsync(config.Id);

    // Assert
    Assert.True(result.Success);
    Assert.Equal(1, result.TracksAdded);

    _mockSpotifyService.Verify(x => x.FindCleanVersionAsync(config.UserId, newTrack), Times.Once);
    _mockSpotifyService.Verify(x => x.AddTracksToPlaylistAsync(
        config.UserId,
        config.TargetPlaylistId,
        It.Is<IEnumerable<string>>(tracks => tracks.Contains($"spotify:track:{cleanTrack.Id}"))), Times.Once);
  }

  [Fact]
  public async Task SyncPlaylistAsync_WithTracksToRemove_ShouldRemoveThem()
  {
    // Arrange
    var config = CreateSyncConfig();
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id))
        .ReturnsAsync(config);
    var sourceTracks = new List<SpotifyTrack>();
    var targetTracks = new List<SpotifyTrack> { CreateSpotifyTrack("clean-1", "Clean Track 1") };
    var mappings = new List<TrackMapping>();
    var delta = new PlaylistDelta
    {
      TracksToAdd = new List<string>(),
      TracksToRemove = new List<string> { "clean-1" },
      NewTracks = new List<SpotifyTrack>(),
      DesiredTrackOrder = new List<string>()
    };

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId))
        .ReturnsAsync(true);
    _mockSpotifyService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.SourcePlaylistId))
        .ReturnsAsync(sourceTracks);
    _mockSpotifyService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.TargetPlaylistId))
        .ReturnsAsync(targetTracks);
    _mockUnitOfWork.Setup(x => x.TrackMappings.GetByJobIdAsync(config.OriginalJobId))
        .ReturnsAsync(mappings);
    _mockDeltaCalculator.Setup(x => x.CalculateDeltaAsync(
        It.IsAny<List<SpotifyTrack>>(),
        It.IsAny<List<SpotifyTrack>>(),
        It.IsAny<List<TrackMapping>>()))
        .ReturnsAsync(delta);
    _mockSyncTimeCalculator.Setup(x => x.CalculateNextSyncTime(It.IsAny<string>(), It.IsAny<DateTime?>()))
        .Returns(DateTime.UtcNow.AddDays(1));

    // Act
    var result = await _syncService.SyncPlaylistAsync(config.Id);

    // Assert
    Assert.True(result.Success);
    Assert.Equal(1, result.TracksRemoved);

    _mockSpotifyService.Verify(x => x.RemoveTracksFromPlaylistAsync(
        config.UserId,
        config.TargetPlaylistId,
        It.Is<IEnumerable<string>>(tracks => tracks.Contains("spotify:track:clean-1"))), Times.Once);
  }

  [Fact]
  public async Task EnableSyncForJobAsync_WithoutActiveSubscription_ShouldThrowException()
  {
    // Arrange
    var userId = 1;
    var jobId = 1;

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(false);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _syncService.EnableSyncForJobAsync(jobId, userId));

    Assert.Contains("subscription", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task EnableSyncForJobAsync_WithValidJob_ShouldCreateSyncConfig()
  {
    // Arrange
    var userId = 1;
    var jobId = 1;
    var job = new CleanPlaylistJob
    {
      Id = jobId,
      UserId = userId,
      SourcePlaylistId = "source123",
      TargetPlaylistId = "target123",
      Status = JobStatus.Completed
    };

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(true);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByJobIdAsync(jobId))
        .ReturnsAsync((PlaylistSyncConfig?)null);
    _mockUnitOfWork.Setup(x => x.Jobs.GetByIdAsync(jobId))
        .ReturnsAsync(job);
    _mockSyncTimeCalculator.Setup(x => x.CalculateNextSyncTime(It.IsAny<string>(), It.IsAny<DateTime?>()))
        .Returns(DateTime.UtcNow.AddDays(1));
    _mockUnitOfWork.Setup(x => x.SyncConfigs.CreateAsync(It.IsAny<PlaylistSyncConfig>()))
        .ReturnsAsync((PlaylistSyncConfig config) => config);

    // Act
    var result = await _syncService.EnableSyncForJobAsync(jobId, userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(userId, result.UserId);
    Assert.Equal(jobId, result.OriginalJobId);
    Assert.Equal("source123", result.SourcePlaylistId);
    Assert.Equal("target123", result.TargetPlaylistId);
    Assert.True(result.IsActive);
    Assert.Equal(SyncFrequency.Daily, result.SyncFrequency);
  }

  [Fact]
  public async Task DisableSyncAsync_WithValidConfig_ShouldDisableConfig()
  {
    // Arrange
    var userId = 1;
    var configId = 1;
    var config = new PlaylistSyncConfig
    {
      Id = configId,
      UserId = userId,
      IsActive = true
    };

    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(configId))
        .ReturnsAsync(config);

    // Act
    var result = await _syncService.DisableSyncAsync(configId, userId);

    // Assert
    Assert.True(result);
    _mockUnitOfWork.Verify(x => x.SyncConfigs.DisableConfigAsync(configId), Times.Once);
  }

  [Fact]
  public async Task ManualSyncAsync_WithInactiveConfig_ShouldThrowException()
  {
    // Arrange
    var userId = 1;
    var configId = 1;
    var config = new PlaylistSyncConfig
    {
      Id = configId,
      UserId = userId,
      IsActive = false
    };

    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(configId))
        .ReturnsAsync(config);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _syncService.ManualSyncAsync(configId, userId));

    Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  private static PlaylistSyncConfig CreateSyncConfig()
  {
    return new PlaylistSyncConfig
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
  }

  private static SpotifyTrack CreateSpotifyTrack(string id, string name)
  {
    return new SpotifyTrack
    {
      Id = id,
      Name = name,
      Artists = new[] { new SpotifyArtist { Name = "Test Artist" } }
    };
  }

  private static TrackMapping CreateTrackMapping(string sourceId, string targetId)
  {
    return new TrackMapping
    {
      SourceTrackId = sourceId,
      TargetTrackId = targetId,
      HasCleanMatch = true,
      JobId = 1,
      SourceTrackName = "Test Track",
      SourceArtistName = "Test Artist",
      CreatedAt = DateTime.UtcNow
    };
  }
}
