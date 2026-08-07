using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class PlaylistSyncServiceTests
{
  private readonly Mock<IUnitOfWork> _mockUnitOfWork;
  private readonly Mock<IMusicServiceFactory> _mockMusicServiceFactory;
  private readonly Mock<IMusicService> _mockMusicService;
  private readonly Mock<IPlaylistDeltaCalculator> _mockDeltaCalculator;
  private readonly Mock<ISubscriptionService> _mockSubscriptionService;
  private readonly Mock<ISyncTimeCalculator> _mockSyncTimeCalculator;
  private readonly Mock<ILogger<PlaylistSyncService>> _mockLogger;
  private readonly PlaylistSyncService _syncService;

  public PlaylistSyncServiceTests()
  {
    _mockUnitOfWork = new Mock<IUnitOfWork>();
    _mockMusicServiceFactory = new Mock<IMusicServiceFactory>();
    _mockMusicService = new Mock<IMusicService>();
    _mockDeltaCalculator = new Mock<IPlaylistDeltaCalculator>();
    _mockSubscriptionService = new Mock<ISubscriptionService>();
    _mockSyncTimeCalculator = new Mock<ISyncTimeCalculator>();
    _mockLogger = new Mock<ILogger<PlaylistSyncService>>();

    _syncService = new PlaylistSyncService(
        _mockUnitOfWork.Object,
        _mockMusicServiceFactory.Object,
        _mockDeltaCalculator.Object,
        _mockSubscriptionService.Object,
        _mockSyncTimeCalculator.Object,
        _mockLogger.Object
    );

    SetupDefaultMocks();
  }

  private void SetupDefaultMocks()
  {
    _mockMusicServiceFactory.Setup(x => x.GetService(It.IsAny<string>()))
        .Returns(_mockMusicService.Object);

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

    _mockUnitOfWork.Setup(x => x.SyncConfigs.DisableConfigAsync(It.IsAny<int>(), It.IsAny<string?>()))
        .Returns(Task.CompletedTask);

    _mockUnitOfWork.Setup(x => x.TrackMappings.AddAsync(It.IsAny<TrackMapping>()))
        .Returns(Task.CompletedTask);

    // A sync config resolves its provider through the job that produced the clean playlist.
    _mockUnitOfWork.Setup(x => x.Jobs.GetByIdAsync(It.IsAny<int>()))
        .ReturnsAsync(CreateOriginalJob());
  }

  [Fact]
  public async Task SyncPlaylistAsync_WithInactiveSubscription_ShouldFailAndDisableConfig()
  {
    var config = CreateSyncConfig();
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id))
        .ReturnsAsync(config);
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId))
        .ReturnsAsync(false);

    var result = await _syncService.SyncPlaylistAsync(config.Id);

    Assert.False(result.Success);
    Assert.Contains("subscription", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

    _mockUnitOfWork.Verify(x => x.SyncConfigs.DisableConfigAsync(config.Id, AutoDisableReason.SubscriptionInactive), Times.Once);
  }

  [Fact]
  public async Task SyncPlaylistAsync_WithActiveSubscription_ShouldPerformSync()
  {
    var config = CreateSyncConfig();
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id))
        .ReturnsAsync(config);
    var sourceTracks = new List<MusicTrack> { CreateTrack("1", "Track 1") };
    var targetTracks = new List<MusicTrack> { CreateTrack("clean-1", "Clean Track 1") };
    var mappings = new List<TrackMapping> { CreateTrackMapping("1", "clean-1") };
    var delta = new PlaylistDelta
    {
      TracksToAdd = new List<string>(),
      TracksToRemove = new List<string>(),
      NewTracks = new List<MusicTrack>(),
      DesiredTrackOrder = new List<string> { "clean-1" }
    };

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId))
        .ReturnsAsync(true);
    _mockMusicService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.SourcePlaylistId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(sourceTracks);
    _mockMusicService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.TargetPlaylistId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(targetTracks);
    _mockUnitOfWork.Setup(x => x.TrackMappings.GetByJobIdAsync(config.OriginalJobId))
        .ReturnsAsync(mappings);
    _mockDeltaCalculator.Setup(x => x.CalculateDeltaAsync(
        It.IsAny<List<MusicTrack>>(),
        It.IsAny<List<MusicTrack>>(),
        It.IsAny<List<TrackMapping>>()))
        .ReturnsAsync(delta);
    _mockSyncTimeCalculator.Setup(x => x.CalculateNextSyncTime(It.IsAny<string>(), It.IsAny<DateTime?>()))
        .Returns(DateTime.UtcNow.AddDays(1));

    var result = await _syncService.SyncPlaylistAsync(config.Id);

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
    var config = CreateSyncConfig();
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id))
        .ReturnsAsync(config);
    var newTrack = CreateTrack("2", "New Track");
    var cleanTrack = CreateTrack("clean-2", "Clean New Track");
    var delta = new PlaylistDelta
    {
      TracksToAdd = new List<string>(),
      TracksToRemove = new List<string>(),
      NewTracks = new List<MusicTrack> { newTrack },
      DesiredTrackOrder = new List<string>()
    };

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId))
        .ReturnsAsync(true);
    _mockMusicService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.SourcePlaylistId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<MusicTrack> { newTrack });
    _mockMusicService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.TargetPlaylistId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<MusicTrack>());
    _mockUnitOfWork.Setup(x => x.TrackMappings.GetByJobIdAsync(config.OriginalJobId))
        .ReturnsAsync(new List<TrackMapping>());
    _mockDeltaCalculator.Setup(x => x.CalculateDeltaAsync(
        It.IsAny<List<MusicTrack>>(),
        It.IsAny<List<MusicTrack>>(),
        It.IsAny<List<TrackMapping>>()))
        .ReturnsAsync(delta);
    _mockMusicService.Setup(x => x.FindCleanVersionAsync(config.UserId, newTrack, It.IsAny<CancellationToken>()))
        .ReturnsAsync(cleanTrack);
    _mockSyncTimeCalculator.Setup(x => x.CalculateNextSyncTime(It.IsAny<string>(), It.IsAny<DateTime?>()))
        .Returns(DateTime.UtcNow.AddDays(1));

    var result = await _syncService.SyncPlaylistAsync(config.Id);

    Assert.True(result.Success);
    Assert.Equal(1, result.TracksAdded);

    _mockMusicService.Verify(x => x.FindCleanVersionAsync(config.UserId, newTrack, It.IsAny<CancellationToken>()), Times.Once);

    // Bare track IDs are passed through — the adapter owns any provider-specific
    // URI formatting, so no "spotify:track:"-style wrapping happens here.
    _mockMusicService.Verify(x => x.AddTracksToPlaylistAsync(
        config.UserId,
        config.TargetPlaylistId,
        It.Is<IEnumerable<string>>(tracks => tracks.Contains(cleanTrack.Id)),
        It.IsAny<CancellationToken>()), Times.Once);
  }

  /// <summary>
  /// Sync is additive. Apple Music's API cannot remove a track from a library playlist, so
  /// a source-side removal must leave the clean playlist untouched and must not be reported
  /// as a removal that happened.
  /// </summary>
  [Fact]
  public async Task SyncPlaylistAsync_WithTracksToRemove_ShouldNotRemoveThemAndShouldReportZero()
  {
    var config = CreateSyncConfig();
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id))
        .ReturnsAsync(config);
    var targetTracks = new List<MusicTrack> { CreateTrack("clean-1", "Clean Track 1") };
    var delta = new PlaylistDelta
    {
      TracksToAdd = new List<string>(),
      TracksToRemove = new List<string> { "clean-1" },
      NewTracks = new List<MusicTrack>(),
      DesiredTrackOrder = new List<string>()
    };

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId))
        .ReturnsAsync(true);
    _mockMusicService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.SourcePlaylistId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<MusicTrack>());
    _mockMusicService.Setup(x => x.GetPlaylistTracksAsync(config.UserId, config.TargetPlaylistId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(targetTracks);
    _mockUnitOfWork.Setup(x => x.TrackMappings.GetByJobIdAsync(config.OriginalJobId))
        .ReturnsAsync(new List<TrackMapping>());
    _mockDeltaCalculator.Setup(x => x.CalculateDeltaAsync(
        It.IsAny<List<MusicTrack>>(),
        It.IsAny<List<MusicTrack>>(),
        It.IsAny<List<TrackMapping>>()))
        .ReturnsAsync(delta);
    _mockSyncTimeCalculator.Setup(x => x.CalculateNextSyncTime(It.IsAny<string>(), It.IsAny<DateTime?>()))
        .Returns(DateTime.UtcNow.AddDays(1));

    var result = await _syncService.SyncPlaylistAsync(config.Id);

    Assert.True(result.Success);

    // The drifted track is still there, so nothing was removed and nothing "changed".
    Assert.Equal(0, result.TracksRemoved);
    Assert.Equal(targetTracks.Count, result.TracksUnchanged);

    // Nothing was written to the playlist at all: no adds either, since the delta had none.
    _mockMusicService.Verify(x => x.AddTracksToPlaylistAsync(
        It.IsAny<int>(),
        It.IsAny<string>(),
        It.IsAny<IEnumerable<string>>(),
        It.IsAny<CancellationToken>()), Times.Never);

    // History must record zero removals, not the drift count.
    _mockUnitOfWork.Verify(x => x.SyncHistory.CompleteHistoryAsync(
        It.IsAny<int>(),
        It.IsAny<int>(),
        0,
        targetTracks.Count,
        It.IsAny<int>()), Times.Once);
  }

  [Fact]
  public async Task SyncPlaylistAsync_ResolvesProviderFromOriginatingJob()
  {
    var config = CreateSyncConfig();
    var job = CreateOriginalJob();
    job.TargetProvider = MusicProviders.AppleMusic;

    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id)).ReturnsAsync(config);
    _mockUnitOfWork.Setup(x => x.Jobs.GetByIdAsync(config.OriginalJobId)).ReturnsAsync(job);
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId)).ReturnsAsync(true);
    _mockMusicService.Setup(x => x.GetPlaylistTracksAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<MusicTrack>());
    _mockDeltaCalculator.Setup(x => x.CalculateDeltaAsync(
        It.IsAny<List<MusicTrack>>(), It.IsAny<List<MusicTrack>>(), It.IsAny<List<TrackMapping>>()))
        .ReturnsAsync(new PlaylistDelta());
    _mockSyncTimeCalculator.Setup(x => x.CalculateNextSyncTime(It.IsAny<string>(), It.IsAny<DateTime?>()))
        .Returns(DateTime.UtcNow.AddDays(1));

    await _syncService.SyncPlaylistAsync(config.Id);

    _mockMusicServiceFactory.Verify(x => x.GetService(MusicProviders.AppleMusic), Times.Once);
  }

  [Fact]
  public async Task SyncPlaylistAsync_WhenOriginatingJobIsMissing_ShouldFail()
  {
    var config = CreateSyncConfig();
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetByIdAsync(config.Id)).ReturnsAsync(config);
    _mockUnitOfWork.Setup(x => x.Jobs.GetByIdAsync(config.OriginalJobId))
        .ReturnsAsync((CleanPlaylistJob?)null);
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(config.UserId)).ReturnsAsync(true);

    var result = await _syncService.SyncPlaylistAsync(config.Id);

    Assert.False(result.Success);
    _mockUnitOfWork.Verify(x => x.SyncHistory.FailHistoryAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Once);
  }

  [Fact]
  public async Task EnableSyncForJobAsync_WithoutActiveSubscription_ShouldThrowException()
  {
    var userId = 1;
    var jobId = 1;

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(false);

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _syncService.EnableSyncForJobAsync(jobId, userId));

    Assert.Contains("subscription", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task EnableSyncForJobAsync_WithValidJob_ShouldCreateSyncConfig()
  {
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

    var result = await _syncService.EnableSyncForJobAsync(jobId, userId);

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

    var result = await _syncService.DisableSyncAsync(configId, userId);

    Assert.True(result);
    _mockUnitOfWork.Verify(x => x.SyncConfigs.DisableConfigAsync(configId, null), Times.Once);
  }

  [Fact]
  public async Task ManualSyncAsync_WithInactiveConfig_ShouldThrowException()
  {
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

  private static CleanPlaylistJob CreateOriginalJob()
  {
    return new CleanPlaylistJob
    {
      Id = 1,
      UserId = 1,
      SourcePlaylistId = "source123",
      TargetPlaylistId = "target123",
      Status = JobStatus.Completed,
      Provider = MusicProviders.AppleMusic,
      TargetProvider = MusicProviders.AppleMusic
    };
  }

  private static MusicTrack CreateTrack(string id, string name)
  {
    return new MusicTrack(
        Id: id,
        Name: name,
        IsExplicit: false,
        Artists: new[] { new MusicArtist("Test Artist") });
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
