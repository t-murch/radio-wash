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
/// Tests for PlaylistCopier — the cross-service engine. Mirrors PlaylistCleanerTests'
/// structure with two provider services resolved through a mock factory. Contracts: tracks
/// are read from the source provider and the playlist is created on the target; the ISRC
/// index is prefetched once over distinct source ISRCs; per-track results persist
/// TrackMapping rows with Isrc and MatchMethod; unmatched tracks count as processed but
/// contribute nothing to the created playlist.
/// </summary>
public class PlaylistCopierTests
{
  private readonly Mock<IMusicService> _source = new();
  private readonly Mock<IMusicService> _target = new();
  private readonly Mock<IMusicServiceFactory> _factory = new();
  private readonly Mock<ITrackMatcher> _matcher = new();
  private readonly Mock<IProgressTracker> _progressTracker = new();
  private readonly Mock<IProgressBroadcastService> _broadcast = new();
  private readonly Mock<IUnitOfWork> _unitOfWork = new();
  private readonly Mock<ITrackMappingRepository> _mappingRepo = new();
  private readonly Mock<ICleanPlaylistJobRepository> _jobRepo = new();
  private readonly PlaylistCopier _copier;

  public PlaylistCopierTests()
  {
    _source.SetupGet(x => x.ProviderName).Returns("spotify");
    _target.SetupGet(x => x.ProviderName).Returns("apple_music");
    _factory.Setup(x => x.GetService("spotify")).Returns(_source.Object);
    _factory.Setup(x => x.GetService("apple_music")).Returns(_target.Object);

    _unitOfWork.Setup(x => x.TrackMappings).Returns(_mappingRepo.Object);
    _unitOfWork.Setup(x => x.Jobs).Returns(_jobRepo.Object);

    _progressTracker.Setup(x => x.ShouldReportProgress(It.IsAny<int>())).Returns(false);
    _progressTracker.Setup(x => x.ShouldPersistProgress(It.IsAny<int>())).Returns(false);
    _progressTracker
      .Setup(x => x.CreateUpdate(It.IsAny<int>(), It.IsAny<string?>()))
      .Returns((int i, string? n) => new ProgressUpdate
      {
        Progress = i,
        ProcessedTracks = i,
        TotalTracks = i,
        CurrentBatch = $"Batch {i}",
        Message = n ?? ""
      });

    _copier = new PlaylistCopier(
      _factory.Object,
      _matcher.Object,
      _progressTracker.Object,
      _broadcast.Object,
      _unitOfWork.Object,
      new Mock<ILogger<PlaylistCopier>>().Object);
  }

  private static CleanPlaylistJob MakeCopyJob(bool swapExplicitForClean = true) => new()
  {
    Id = 11,
    UserId = 7,
    Provider = "spotify",
    TargetProvider = "apple_music",
    JobType = JobTypes.Copy,
    SwapExplicitForClean = swapExplicitForClean,
    SourcePlaylistId = "src-pl",
    SourcePlaylistName = "Road Trip",
    TargetPlaylistName = "Road Trip",
    Status = JobStatus.Pending
  };

  private static MusicTrack SourceTrack(string id, string name, bool isExplicit = false, string? isrc = null) =>
    new(id, name, isExplicit, new List<MusicArtist> { new("Artist") }, isrc);

  private static MusicTrack TargetTrack(string id, string name) =>
    new(id, name, false, new List<MusicArtist> { new("Artist") });

  [Fact]
  public async Task CopyPlaylistAsync_CapsIsrcPrefetchButStillProcessesEveryTrack()
  {
    // Spotify spends one search per ISRC, so an uncapped prefetch front-loads hundreds of
    // sequential calls. Tracks past the cap must still be matched — they just fall through
    // to the matcher's search fallback instead of the ISRC index.
    const int cap = 200;
    var job = MakeCopyJob();
    var user = new User { Id = 7, SupabaseId = "sb" };
    var tracks = Enumerable.Range(0, cap + 25)
      .Select(i => SourceTrack($"s{i}", $"Track {i}", isrc: $"ISRC{i:D4}"))
      .ToArray();

    _source.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src-pl", It.IsAny<CancellationToken>()))
      .ReturnsAsync(tracks);

    IReadOnlyCollection<string>? prefetchedIsrcs = null;
    _matcher
      .Setup(x => x.PrefetchByIsrcAsync(user.Id, _target.Object, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
      .Callback<int, IMusicService, IReadOnlyCollection<string>, CancellationToken>((_, _, isrcs, _) => prefetchedIsrcs = isrcs)
      .ReturnsAsync(new Dictionary<string, MusicTrack>());

    _matcher
      .Setup(x => x.MatchAsync(user.Id, _target.Object, It.IsAny<MusicTrack>(),
        It.IsAny<IReadOnlyDictionary<string, MusicTrack>>(), true, It.IsAny<CancellationToken>()))
      .ReturnsAsync((int _, IMusicService _, MusicTrack t, IReadOnlyDictionary<string, MusicTrack> _, bool _, CancellationToken _) =>
        new TrackMatch(t, TargetTrack($"t-{t.Id}", t.Name), MatchMethods.Search));

    _target.Setup(x => x.CreatePlaylistAsync(user.Id, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PlaylistSummary("p.new", "Road Trip", null, null, 0, "", null));

    var result = await _copier.CopyPlaylistAsync(job, user);

    Assert.Equal(cap, prefetchedIsrcs!.Count);
    // Every track is still processed and matched, cap notwithstanding.
    Assert.Equal(tracks.Length, result.ProcessedTracks);
    Assert.Equal(tracks.Length, result.MatchedTracks);
  }

  [Fact]
  public async Task CopyPlaylistAsync_ReadsFromSourceMatchesAndWritesToTarget()
  {
    var job = MakeCopyJob();
    var user = new User { Id = 7, SupabaseId = "sb" };
    var tracks = new[]
    {
      SourceTrack("s1", "One", isrc: "ISRC1"),
      SourceTrack("s2", "Two", isrc: "ISRC2"),
      SourceTrack("s3", "Three") // no ISRC → search path inside matcher
    };

    _source.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src-pl", It.IsAny<CancellationToken>()))
      .ReturnsAsync(tracks);

    IReadOnlyCollection<string>? prefetchedIsrcs = null;
    var isrcIndex = new Dictionary<string, MusicTrack> { ["ISRC1"] = TargetTrack("a1", "One") };
    _matcher
      .Setup(x => x.PrefetchByIsrcAsync(user.Id, _target.Object, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
      .Callback<int, IMusicService, IReadOnlyCollection<string>, CancellationToken>((_, _, isrcs, _) => prefetchedIsrcs = isrcs)
      .ReturnsAsync(isrcIndex);

    _matcher.Setup(x => x.MatchAsync(user.Id, _target.Object, tracks[0], isrcIndex, true, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new TrackMatch(tracks[0], TargetTrack("a1", "One"), MatchMethods.Isrc));
    _matcher.Setup(x => x.MatchAsync(user.Id, _target.Object, tracks[1], isrcIndex, true, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new TrackMatch(tracks[1], TargetTrack("a2", "Two"), MatchMethods.Search));
    _matcher.Setup(x => x.MatchAsync(user.Id, _target.Object, tracks[2], isrcIndex, true, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new TrackMatch(tracks[2], null, MatchMethods.None));

    _target.Setup(x => x.CreatePlaylistAsync(user.Id, "Road Trip", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PlaylistSummary("p.new", "Road Trip", null, null, 0, "", null));

    IEnumerable<string>? addedIds = null;
    _target.Setup(x => x.AddTracksToPlaylistAsync(user.Id, "p.new", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
      .Callback<int, string, IEnumerable<string>, CancellationToken>((_, _, ids, _) => addedIds = ids.ToList())
      .Returns(Task.CompletedTask);

    List<TrackMapping>? persisted = null;
    _mappingRepo
      .Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<TrackMapping>>()))
      .Callback<IEnumerable<TrackMapping>>(m => persisted = m.ToList())
      .Returns(Task.CompletedTask);

    var result = await _copier.CopyPlaylistAsync(job, user);

    // ISRC prefetch happened once over the distinct source ISRCs.
    Assert.Equal(new[] { "ISRC1", "ISRC2" }, prefetchedIsrcs!.OrderBy(x => x));

    Assert.Equal(3, result.ProcessedTracks);
    Assert.Equal(2, result.MatchedTracks);
    Assert.Equal("p.new", result.TargetPlaylistId);
    Assert.Equal(new[] { "a1", "a2" }, addedIds);

    // Mapping rows carry the match diagnostics.
    Assert.NotNull(persisted);
    Assert.Equal(3, persisted!.Count);
    Assert.Equal(MatchMethods.Isrc, persisted[0].MatchMethod);
    Assert.Equal("ISRC1", persisted[0].Isrc);
    Assert.Equal("a1", persisted[0].TargetTrackId);
    Assert.True(persisted[0].HasCleanMatch);
    Assert.Equal(MatchMethods.None, persisted[2].MatchMethod);
    Assert.Null(persisted[2].TargetTrackId);
    Assert.False(persisted[2].HasCleanMatch);

    // Source service is never written to; target service is never read for tracks.
    _source.Verify(x => x.CreatePlaylistAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    _target.Verify(x => x.GetPlaylistTracksAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task CopyPlaylistAsync_PassesSwapToggleThroughToMatcher()
  {
    var job = MakeCopyJob(swapExplicitForClean: false);
    var user = new User { Id = 7, SupabaseId = "sb" };
    var track = SourceTrack("s1", "One", isExplicit: true);

    _source.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src-pl", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new[] { track });
    _matcher
      .Setup(x => x.PrefetchByIsrcAsync(user.Id, _target.Object, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new Dictionary<string, MusicTrack>());
    _matcher
      .Setup(x => x.MatchAsync(user.Id, _target.Object, track, It.IsAny<IReadOnlyDictionary<string, MusicTrack>>(), false, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new TrackMatch(track, TargetTrack("a1", "One"), MatchMethods.Search));
    _target.Setup(x => x.CreatePlaylistAsync(user.Id, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PlaylistSummary("p.new", "Road Trip", null, null, 0, "", null));

    var result = await _copier.CopyPlaylistAsync(job, user);

    Assert.Equal(1, result.MatchedTracks);
    _matcher.Verify(x => x.MatchAsync(user.Id, _target.Object, track,
      It.IsAny<IReadOnlyDictionary<string, MusicTrack>>(), false, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task CopyPlaylistAsync_AllUnmatched_CreatesEmptyPlaylistWithoutAddCall()
  {
    var job = MakeCopyJob();
    var user = new User { Id = 7, SupabaseId = "sb" };
    var track = SourceTrack("s1", "Obscure B-Side");

    _source.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src-pl", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new[] { track });
    _matcher
      .Setup(x => x.PrefetchByIsrcAsync(user.Id, _target.Object, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new Dictionary<string, MusicTrack>());
    _matcher
      .Setup(x => x.MatchAsync(user.Id, _target.Object, track, It.IsAny<IReadOnlyDictionary<string, MusicTrack>>(), true, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new TrackMatch(track, null, MatchMethods.None));
    _target.Setup(x => x.CreatePlaylistAsync(user.Id, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PlaylistSummary("p.empty", "Road Trip", null, null, 0, "", null));

    var result = await _copier.CopyPlaylistAsync(job, user);

    Assert.Equal(1, result.ProcessedTracks);
    Assert.Equal(0, result.MatchedTracks);
    _target.Verify(
      x => x.AddTracksToPlaylistAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
      Times.Never);
  }

  [Fact]
  public async Task CopyPlaylistAsync_BatchPersistenceFailure_RollsBackAndRethrows()
  {
    var job = MakeCopyJob();
    var user = new User { Id = 7, SupabaseId = "sb" };
    var track = SourceTrack("s1", "One", isrc: "ISRC1");

    _source.Setup(x => x.GetPlaylistTracksAsync(user.Id, "src-pl", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new[] { track });
    _matcher
      .Setup(x => x.PrefetchByIsrcAsync(user.Id, _target.Object, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new Dictionary<string, MusicTrack>());
    _matcher
      .Setup(x => x.MatchAsync(user.Id, _target.Object, track, It.IsAny<IReadOnlyDictionary<string, MusicTrack>>(), true, It.IsAny<CancellationToken>()))
      .ReturnsAsync(new TrackMatch(track, TargetTrack("a1", "One"), MatchMethods.Isrc));

    _progressTracker.Setup(x => x.ShouldPersistProgress(1)).Returns(true);
    _mappingRepo
      .Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<TrackMapping>>()))
      .ThrowsAsync(new InvalidOperationException("db down"));

    await Assert.ThrowsAsync<InvalidOperationException>(() => _copier.CopyPlaylistAsync(job, user));

    _unitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
    _target.Verify(x => x.CreatePlaylistAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
  }
}
