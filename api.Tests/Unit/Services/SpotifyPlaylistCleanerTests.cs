using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Characterization tests for <see cref="SpotifyPlaylistCleaner"/>. The cleaner now consumes
/// the provider-agnostic <c>IMusicService</c>; the Spotify-specific URI format that used to
/// live here moved to <c>SpotifyMusicServiceTests</c>. The remaining tests pin the
/// processing-loop behavior — explicit-vs-clean counting, invalid-track skip, batch
/// persistence/rollback, and broadcast-failure tolerance — so the Phase 2 refactor can't
/// silently regress the critical path.
/// </summary>
public class SpotifyPlaylistCleanerTests
{
  private readonly Mock<IMusicService> _mockMusic;
  private readonly Mock<IProgressTracker> _mockProgressTracker;
  private readonly Mock<IProgressBroadcastService> _mockBroadcast;
  private readonly Mock<IUnitOfWork> _mockUnitOfWork;
  private readonly Mock<ILogger<SpotifyPlaylistCleaner>> _mockLogger;
  private readonly Mock<ITrackMappingRepository> _mockMappingRepo;
  private readonly Mock<ICleanPlaylistJobRepository> _mockJobRepo;
  private readonly SpotifyPlaylistCleaner _cleaner;

  public SpotifyPlaylistCleanerTests()
  {
    _mockMusic = new Mock<IMusicService>();
    _mockProgressTracker = new Mock<IProgressTracker>();
    _mockBroadcast = new Mock<IProgressBroadcastService>();
    _mockUnitOfWork = new Mock<IUnitOfWork>();
    _mockLogger = new Mock<ILogger<SpotifyPlaylistCleaner>>();
    _mockMappingRepo = new Mock<ITrackMappingRepository>();
    _mockJobRepo = new Mock<ICleanPlaylistJobRepository>();

    _mockUnitOfWork.Setup(x => x.TrackMappings).Returns(_mockMappingRepo.Object);
    _mockUnitOfWork.Setup(x => x.Jobs).Returns(_mockJobRepo.Object);

    _mockProgressTracker.Setup(x => x.ShouldReportProgress(It.IsAny<int>())).Returns(false);
    _mockProgressTracker.Setup(x => x.ShouldPersistProgress(It.IsAny<int>())).Returns(false);
    _mockProgressTracker
      .Setup(x => x.CreateUpdate(It.IsAny<int>(), It.IsAny<string?>()))
      .Returns((int i, string? n) => new ProgressUpdate
      {
        Progress = i,
        ProcessedTracks = i,
        TotalTracks = i,
        CurrentBatch = $"Batch {i}",
        Message = n ?? ""
      });

    _cleaner = new SpotifyPlaylistCleaner(
      _mockMusic.Object,
      _mockProgressTracker.Object,
      _mockBroadcast.Object,
      _mockUnitOfWork.Object,
      _mockLogger.Object);
  }

  [Fact]
  public async Task CleanPlaylistAsync_AllExplicitWithAllCleanMatches_MatchesEveryTrack()
  {
    var job = MakeJob(id: 1, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    var tracks = new MusicTrack[]
    {
      MakeTrack("t1", "Song 1", isExplicit: true),
      MakeTrack("t2", "Song 2", isExplicit: true),
      MakeTrack("t3", "Song 3", isExplicit: true)
    };
    var cleanVersions = new Dictionary<string, MusicTrack>
    {
      ["t1"] = MakeTrack("c1", "Song 1", isExplicit: false),
      ["t2"] = MakeTrack("c2", "Song 2", isExplicit: false),
      ["t3"] = MakeTrack("c3", "Song 3", isExplicit: false)
    };

    _mockMusic.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src", It.IsAny<CancellationToken>()))
      .ReturnsAsync(tracks);
    foreach (var (sourceId, clean) in cleanVersions)
    {
      _mockMusic
        .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<MusicTrack>(t => t.Id == sourceId), It.IsAny<CancellationToken>()))
        .ReturnsAsync(clean);
    }
    _mockMusic
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PlaylistSummary("target-id", job.TargetPlaylistName, null, null, 0, "owner", null));

    IEnumerable<string>? observedIds = null;
    _mockMusic
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
      .Callback<int, string, IEnumerable<string>, CancellationToken>((_, _, ids, _) => observedIds = ids.ToList())
      .Returns(Task.CompletedTask);

    var result = await _cleaner.CleanPlaylistAsync(job, user);

    Assert.Equal(3, result.ProcessedTracks);
    Assert.Equal(3, result.MatchedTracks);
    Assert.Equal("target-id", result.TargetPlaylistId);
    Assert.Equal(new[] { "c1", "c2", "c3" }, result.CleanTrackUris);

    // The cleaner passes raw platform IDs to the adapter. The URI-format assertion lives in
    // SpotifyMusicServiceTests.
    Assert.NotNull(observedIds);
    Assert.Equal(new[] { "c1", "c2", "c3" }, observedIds);
  }

  [Fact]
  public async Task CleanPlaylistAsync_MixedExplicitAndClean_CountsProcessedButOnlyMatchesExplicit()
  {
    var job = MakeJob(id: 2, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    var explicitWithMatch = MakeTrack("e1", "Explicit With Match", isExplicit: true);
    var explicitNoMatch = MakeTrack("e2", "Explicit No Match", isExplicit: true);
    var alreadyClean = MakeTrack("c1", "Already Clean", isExplicit: false);

    _mockMusic.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new[] { explicitWithMatch, explicitNoMatch, alreadyClean });

    _mockMusic
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<MusicTrack>(t => t.Id == "e1"), It.IsAny<CancellationToken>()))
      .ReturnsAsync(MakeTrack("e1-clean", "Explicit With Match", isExplicit: false));
    _mockMusic
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<MusicTrack>(t => t.Id == "e2"), It.IsAny<CancellationToken>()))
      .ReturnsAsync((MusicTrack?)null);
    _mockMusic
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<MusicTrack>(t => t.Id == "c1"), It.IsAny<CancellationToken>()))
      .ReturnsAsync(alreadyClean);

    _mockMusic
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PlaylistSummary("target-id", job.TargetPlaylistName, null, null, 0, "owner", null));

    IEnumerable<string>? observedIds = null;
    _mockMusic
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
      .Callback<int, string, IEnumerable<string>, CancellationToken>((_, _, ids, _) => observedIds = ids.ToList())
      .Returns(Task.CompletedTask);

    var result = await _cleaner.CleanPlaylistAsync(job, user);

    Assert.Equal(3, result.ProcessedTracks);
    Assert.Equal(2, result.MatchedTracks);
    Assert.NotNull(observedIds);
    Assert.Equal(new[] { "e1-clean", "c1" }, observedIds);
  }

  [Fact]
  public async Task CleanPlaylistAsync_InvalidTrackWithEmptyId_SkipsWithWarningAndDoesNotCount()
  {
    var job = MakeJob(id: 3, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    var validTrack = MakeTrack("ok", "Good Song", isExplicit: false);
    var invalidTrack = MakeTrack(id: "", name: "Malformed", isExplicit: false);

    _mockMusic.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new[] { validTrack, invalidTrack });
    _mockMusic
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<MusicTrack>(t => t.Id == "ok"), It.IsAny<CancellationToken>()))
      .ReturnsAsync(validTrack);
    _mockMusic
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PlaylistSummary("target-id", job.TargetPlaylistName, null, null, 0, "owner", null));
    _mockMusic
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    var result = await _cleaner.CleanPlaylistAsync(job, user);

    Assert.Equal(1, result.ProcessedTracks);
    Assert.Equal(1, result.MatchedTracks);

    _mockLogger.Verify(
      l => l.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.AtLeastOnce);

    _mockMusic.Verify(
      x => x.FindCleanVersionAsync(user.Id, It.Is<MusicTrack>(t => t.Id == ""), It.IsAny<CancellationToken>()),
      Times.Never);
  }

  [Fact]
  public async Task CleanPlaylistAsync_BatchPersistenceAtThreshold_CommitsTransactionAndUpdatesProgress()
  {
    var job = MakeJob(id: 4, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    var tracks = new[]
    {
      MakeTrack("t1", "S1", isExplicit: false),
      MakeTrack("t2", "S2", isExplicit: false)
    };

    _mockMusic.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src", It.IsAny<CancellationToken>()))
      .ReturnsAsync(tracks);
    _mockMusic
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.IsAny<MusicTrack>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((int _, MusicTrack t, CancellationToken _) => t);
    _mockMusic
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PlaylistSummary("target-id", job.TargetPlaylistName, null, null, 0, "owner", null));
    _mockMusic
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    _mockProgressTracker
      .Setup(x => x.ShouldPersistProgress(It.IsAny<int>()))
      .Returns((int current) => current == 1);

    await _cleaner.CleanPlaylistAsync(job, user);

    _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.AtLeastOnce);
    _mockMappingRepo.Verify(x => x.AddRangeAsync(It.IsAny<IEnumerable<TrackMapping>>()), Times.AtLeastOnce);
    _mockJobRepo.Verify(
      x => x.UpdateProgressAsync(job.Id, It.IsAny<int>(), It.IsAny<string?>()),
      Times.AtLeastOnce);
    _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.AtLeastOnce);
    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Never);
  }

  [Fact]
  public async Task CleanPlaylistAsync_BatchPersistenceThrows_RollsBackAndRethrows()
  {
    var job = MakeJob(id: 5, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    _mockMusic
      .Setup(x => x.GetPlaylistTracksAsync(user.Id, "src", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new[] { MakeTrack("t1", "S1", isExplicit: false) });
    _mockMusic
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.IsAny<MusicTrack>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((int _, MusicTrack t, CancellationToken _) => t);

    _mockProgressTracker.Setup(x => x.ShouldPersistProgress(It.IsAny<int>())).Returns(true);
    _mockMappingRepo
      .Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<TrackMapping>>()))
      .ThrowsAsync(new InvalidOperationException("DB exploded"));

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => _cleaner.CleanPlaylistAsync(job, user));

    _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
    _mockLogger.Verify(
      l => l.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.AtLeastOnce);
  }

  [Fact]
  public async Task CleanPlaylistAsync_BroadcastFailureDuringProgressReporting_LogsButDoesNotFail()
  {
    var job = MakeJob(id: 6, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    _mockMusic
      .Setup(x => x.GetPlaylistTracksAsync(user.Id, "src", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new[] { MakeTrack("t1", "S1", isExplicit: false) });
    _mockMusic
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.IsAny<MusicTrack>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((int _, MusicTrack t, CancellationToken _) => t);
    _mockMusic
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PlaylistSummary("target-id", job.TargetPlaylistName, null, null, 0, "owner", null));
    _mockMusic
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    _mockProgressTracker.Setup(x => x.ShouldReportProgress(It.IsAny<int>())).Returns(true);
    _mockBroadcast
      .Setup(x => x.BroadcastProgressUpdate(job.Id, It.IsAny<ProgressUpdate>()))
      .ThrowsAsync(new Exception("SignalR unreachable"));

    var result = await _cleaner.CleanPlaylistAsync(job, user);

    Assert.Equal(1, result.ProcessedTracks);
    Assert.Equal("target-id", result.TargetPlaylistId);

    _mockLogger.Verify(
      l => l.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.AtLeastOnce);
  }

  // --- Helpers ---

  private static CleanPlaylistJob MakeJob(int id, int userId, string sourceId) => new()
  {
    Id = id,
    UserId = userId,
    Provider = "spotify",
    SourcePlaylistId = sourceId,
    SourcePlaylistName = "Source Playlist",
    TargetPlaylistName = "Clean - Source Playlist",
    Status = JobStatus.Processing,
    TotalTracks = 0
  };

  private static MusicTrack MakeTrack(string id, string name, bool isExplicit) =>
    new(id, name, isExplicit, new[] { new MusicArtist("Artist") });
}
