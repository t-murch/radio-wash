using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Characterization tests for <see cref="SpotifyPlaylistCleaner"/>. These tests pin the
/// observable behavior of the per-track processing loop — including clean-version lookup,
/// track-mapping persistence, progress reporting, and target-playlist creation — as it exists
/// today. The multi-platform refactor in Phase 2 will swap <see cref="ISpotifyService"/> for a
/// generic <c>IMusicService</c>; these tests must continue to pass with adjusted test doubles.
/// </summary>
public class SpotifyPlaylistCleanerTests
{
  private readonly Mock<ISpotifyService> _mockSpotify;
  private readonly Mock<IProgressTracker> _mockProgressTracker;
  private readonly Mock<IProgressBroadcastService> _mockBroadcast;
  private readonly Mock<IUnitOfWork> _mockUnitOfWork;
  private readonly Mock<ILogger<SpotifyPlaylistCleaner>> _mockLogger;
  private readonly Mock<ITrackMappingRepository> _mockMappingRepo;
  private readonly Mock<ICleanPlaylistJobRepository> _mockJobRepo;
  private readonly SpotifyPlaylistCleaner _cleaner;

  public SpotifyPlaylistCleanerTests()
  {
    _mockSpotify = new Mock<ISpotifyService>();
    _mockProgressTracker = new Mock<IProgressTracker>();
    _mockBroadcast = new Mock<IProgressBroadcastService>();
    _mockUnitOfWork = new Mock<IUnitOfWork>();
    _mockLogger = new Mock<ILogger<SpotifyPlaylistCleaner>>();
    _mockMappingRepo = new Mock<ITrackMappingRepository>();
    _mockJobRepo = new Mock<ICleanPlaylistJobRepository>();

    _mockUnitOfWork.Setup(x => x.TrackMappings).Returns(_mockMappingRepo.Object);
    _mockUnitOfWork.Setup(x => x.Jobs).Returns(_mockJobRepo.Object);

    // Default: never trigger mid-loop persistence/reporting so we isolate terminal behavior.
    // Individual tests override these to exercise batch-threshold paths.
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
      _mockSpotify.Object,
      _mockProgressTracker.Object,
      _mockBroadcast.Object,
      _mockUnitOfWork.Object,
      _mockLogger.Object);
  }

  [Fact]
  public async Task CleanPlaylistAsync_AllExplicitWithAllCleanMatches_MatchesEveryTrack()
  {
    // Arrange
    var job = MakeJob(id: 1, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    var tracks = new[]
    {
      MakeTrack("t1", "Song 1", isExplicit: true),
      MakeTrack("t2", "Song 2", isExplicit: true),
      MakeTrack("t3", "Song 3", isExplicit: true)
    };
    var cleanVersions = new Dictionary<string, SpotifyTrack>
    {
      ["t1"] = MakeTrack("c1", "Song 1", isExplicit: false),
      ["t2"] = MakeTrack("c2", "Song 2", isExplicit: false),
      ["t3"] = MakeTrack("c3", "Song 3", isExplicit: false)
    };

    _mockSpotify.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src")).ReturnsAsync(tracks);
    foreach (var (sourceId, clean) in cleanVersions)
    {
      _mockSpotify
        .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<SpotifyTrack>(t => t.Id == sourceId)))
        .ReturnsAsync(clean);
    }
    _mockSpotify
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>()))
      .ReturnsAsync(new SpotifyPlaylist { Id = "target-id", Name = job.TargetPlaylistName });

    IEnumerable<string>? observedUris = null;
    _mockSpotify
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>()))
      .Callback<int, string, IEnumerable<string>>((_, _, uris) => observedUris = uris.ToList())
      .Returns(Task.CompletedTask);

    // Act
    var result = await _cleaner.CleanPlaylistAsync(job, user);

    // Assert
    Assert.Equal(3, result.ProcessedTracks);
    Assert.Equal(3, result.MatchedTracks);
    Assert.Equal("target-id", result.TargetPlaylistId);
    Assert.Equal(new[] { "c1", "c2", "c3" }, result.CleanTrackUris);

    // Assert — the URI format passed to Spotify is `spotify:track:<id>`. The refactor will move
    // this assertion into SpotifyMusicServiceTests; the cleaner will then pass raw IDs.
    Assert.NotNull(observedUris);
    Assert.Equal(
      new[] { "spotify:track:c1", "spotify:track:c2", "spotify:track:c3" },
      observedUris);
  }

  [Fact]
  public async Task CleanPlaylistAsync_MixedExplicitAndClean_CountsProcessedButOnlyMatchesExplicit()
  {
    // Arrange
    var job = MakeJob(id: 2, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    var explicitWithMatch = MakeTrack("e1", "Explicit With Match", isExplicit: true);
    var explicitNoMatch = MakeTrack("e2", "Explicit No Match", isExplicit: true);
    var alreadyClean = MakeTrack("c1", "Already Clean", isExplicit: false);

    _mockSpotify
      .Setup(x => x.GetPlaylistTracksAsync(user.Id, "src"))
      .ReturnsAsync(new[] { explicitWithMatch, explicitNoMatch, alreadyClean });

    _mockSpotify
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<SpotifyTrack>(t => t.Id == "e1")))
      .ReturnsAsync(MakeTrack("e1-clean", "Explicit With Match", isExplicit: false));
    _mockSpotify
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<SpotifyTrack>(t => t.Id == "e2")))
      .ReturnsAsync((SpotifyTrack?)null);
    // Non-explicit: SpotifyService today returns the track itself (IsExplicit=false path).
    _mockSpotify
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<SpotifyTrack>(t => t.Id == "c1")))
      .ReturnsAsync(alreadyClean);

    _mockSpotify
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>()))
      .ReturnsAsync(new SpotifyPlaylist { Id = "target-id" });

    IEnumerable<string>? observedUris = null;
    _mockSpotify
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>()))
      .Callback<int, string, IEnumerable<string>>((_, _, uris) => observedUris = uris.ToList())
      .Returns(Task.CompletedTask);

    // Act
    var result = await _cleaner.CleanPlaylistAsync(job, user);

    // Assert — all three counted as processed; only the two with clean matches contribute URIs.
    Assert.Equal(3, result.ProcessedTracks);
    Assert.Equal(2, result.MatchedTracks);
    Assert.NotNull(observedUris);
    Assert.Equal(new[] { "spotify:track:e1-clean", "spotify:track:c1" }, observedUris);
  }

  [Fact]
  public async Task CleanPlaylistAsync_InvalidTrackWithEmptyId_SkipsWithWarningAndDoesNotCount()
  {
    // Arrange
    var job = MakeJob(id: 3, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    var validTrack = MakeTrack("ok", "Good Song", isExplicit: false);
    var invalidTrack = MakeTrack(id: "", name: "Malformed", isExplicit: false);

    _mockSpotify
      .Setup(x => x.GetPlaylistTracksAsync(user.Id, "src"))
      .ReturnsAsync(new[] { validTrack, invalidTrack });
    _mockSpotify
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.Is<SpotifyTrack>(t => t.Id == "ok")))
      .ReturnsAsync(validTrack);
    _mockSpotify
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>()))
      .ReturnsAsync(new SpotifyPlaylist { Id = "target-id" });
    _mockSpotify
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>()))
      .Returns(Task.CompletedTask);

    // Act
    var result = await _cleaner.CleanPlaylistAsync(job, user);

    // Assert — invalid track skipped; the loop's `continue` means neither processed nor matched
    // counters tick for it. Only the one valid track shows up.
    Assert.Equal(1, result.ProcessedTracks);
    Assert.Equal(1, result.MatchedTracks);

    // Assert — warning logged for the skip
    _mockLogger.Verify(
      l => l.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.AtLeastOnce);

    // Assert — FindCleanVersionAsync was never invoked for the invalid track
    _mockSpotify.Verify(
      x => x.FindCleanVersionAsync(user.Id, It.Is<SpotifyTrack>(t => t.Id == "")),
      Times.Never);
  }

  [Fact]
  public async Task CleanPlaylistAsync_BatchPersistenceAtThreshold_CommitsTransactionAndUpdatesProgress()
  {
    // Arrange
    var job = MakeJob(id: 4, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    var tracks = new[]
    {
      MakeTrack("t1", "S1", isExplicit: false),
      MakeTrack("t2", "S2", isExplicit: false)
    };

    _mockSpotify.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src")).ReturnsAsync(tracks);
    _mockSpotify
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.IsAny<SpotifyTrack>()))
      .ReturnsAsync((int _, SpotifyTrack t) => t);
    _mockSpotify
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>()))
      .ReturnsAsync(new SpotifyPlaylist { Id = "target-id" });
    _mockSpotify
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>()))
      .Returns(Task.CompletedTask);

    // Force persistence to trigger at the first track
    _mockProgressTracker
      .Setup(x => x.ShouldPersistProgress(It.IsAny<int>()))
      .Returns((int current) => current == 1);

    // Act
    await _cleaner.CleanPlaylistAsync(job, user);

    // Assert — the batch path opened a transaction, persisted the mapping, updated progress,
    // saved, and committed.
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
    // Arrange
    var job = MakeJob(id: 5, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    _mockSpotify
      .Setup(x => x.GetPlaylistTracksAsync(user.Id, "src"))
      .ReturnsAsync(new[] { MakeTrack("t1", "S1", isExplicit: false) });
    _mockSpotify
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.IsAny<SpotifyTrack>()))
      .ReturnsAsync((int _, SpotifyTrack t) => t);

    _mockProgressTracker.Setup(x => x.ShouldPersistProgress(It.IsAny<int>())).Returns(true);
    _mockMappingRepo
      .Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<TrackMapping>>()))
      .ThrowsAsync(new InvalidOperationException("DB exploded"));

    // Act + Assert — exception surfaces to caller (processor catches it and marks the job failed)
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
    // Arrange
    var job = MakeJob(id: 6, userId: 7, sourceId: "src");
    var user = new User { Id = 7, SupabaseId = "sb" };
    _mockSpotify
      .Setup(x => x.GetPlaylistTracksAsync(user.Id, "src"))
      .ReturnsAsync(new[] { MakeTrack("t1", "S1", isExplicit: false) });
    _mockSpotify
      .Setup(x => x.FindCleanVersionAsync(user.Id, It.IsAny<SpotifyTrack>()))
      .ReturnsAsync((int _, SpotifyTrack t) => t);
    _mockSpotify
      .Setup(x => x.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, It.IsAny<string?>()))
      .ReturnsAsync(new SpotifyPlaylist { Id = "target-id" });
    _mockSpotify
      .Setup(x => x.AddTracksToPlaylistAsync(user.Id, "target-id", It.IsAny<IEnumerable<string>>()))
      .Returns(Task.CompletedTask);

    _mockProgressTracker.Setup(x => x.ShouldReportProgress(It.IsAny<int>())).Returns(true);
    _mockBroadcast
      .Setup(x => x.BroadcastProgressUpdate(job.Id, It.IsAny<ProgressUpdate>()))
      .ThrowsAsync(new Exception("SignalR unreachable"));

    // Act — must not throw
    var result = await _cleaner.CleanPlaylistAsync(job, user);

    // Assert — cleaning completed successfully despite the broadcast failure
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
    SourcePlaylistId = sourceId,
    SourcePlaylistName = "Source Playlist",
    TargetPlaylistName = "Clean - Source Playlist",
    Status = JobStatus.Processing,
    TotalTracks = 0
  };

  private static SpotifyTrack MakeTrack(string id, string name, bool isExplicit) => new()
  {
    Id = id,
    Name = name,
    Explicit = isExplicit,
    Artists = new[] { new SpotifyArtist { Id = "a1", Name = "Artist" } },
    Album = new SpotifyAlbum { Id = "alb1", Name = "Album" },
    Uri = $"spotify:track:{id}"
  };
}
