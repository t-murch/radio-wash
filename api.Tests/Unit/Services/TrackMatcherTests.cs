using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Tests for TrackMatcher — the cross-catalog bridge. Contracts: ISRC hits win outright for
/// faithful copies; the clean toggle swaps explicit ISRC hits for the target's clean version
/// and leaves the track unmatched when none exists (mirroring same-service clean semantics);
/// the search fallback filters on normalized name, artist overlap, and duration; a faithful
/// copy prefers the candidate matching the source's explicitness.
/// </summary>
public class TrackMatcherTests
{
  private const int UserId = 7;

  private readonly Mock<IMusicService> _target = new();
  private readonly TrackMatcher _matcher;

  public TrackMatcherTests()
  {
    _target.SetupGet(x => x.ProviderName).Returns("apple_music");
    _matcher = new TrackMatcher(new Mock<ILogger<TrackMatcher>>().Object);
  }

  private static MusicTrack Source(
    string id = "src1", string name = "Song", bool isExplicit = true,
    string artist = "Artist", string? isrc = null, int? durationMs = null) =>
      new(id, name, isExplicit, new List<MusicArtist> { new(artist) }, isrc, durationMs);

  private static MusicTrack TargetTrack(
    string id, string name = "Song", bool isExplicit = false,
    string artist = "Artist", int? durationMs = null) =>
      new(id, name, isExplicit, new List<MusicArtist> { new(artist) }, DurationMs: durationMs);

  private static IReadOnlyDictionary<string, MusicTrack> Index(params (string isrc, MusicTrack track)[] entries) =>
      entries.ToDictionary(e => e.isrc, e => e.track, StringComparer.OrdinalIgnoreCase);

  [Fact]
  public async Task MatchAsync_IsrcHit_FaithfulCopy_TakesHitEvenWhenExplicit()
  {
    var source = Source(isrc: "ISRC1", isExplicit: true);
    var hit = TargetTrack("tgt1", isExplicit: true);

    var match = await _matcher.MatchAsync(UserId, _target.Object, source,
        Index(("ISRC1", hit)), preferClean: false, CancellationToken.None);

    Assert.Same(hit, match.Target);
    Assert.Equal(MatchMethods.Isrc, match.Method);
    _target.Verify(x => x.SearchTracksAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task MatchAsync_IsrcHitClean_PreferCleanTakesHitDirectly()
  {
    var source = Source(isrc: "ISRC1", isExplicit: true);
    var cleanHit = TargetTrack("tgt1", isExplicit: false);

    var match = await _matcher.MatchAsync(UserId, _target.Object, source,
        Index(("ISRC1", cleanHit)), preferClean: true, CancellationToken.None);

    Assert.Same(cleanHit, match.Target);
    Assert.Equal(MatchMethods.Isrc, match.Method);
  }

  [Fact]
  public async Task MatchAsync_ExplicitIsrcHitWithCleanToggle_SwapsForCleanVersion()
  {
    var source = Source(isrc: "ISRC1", isExplicit: true);
    var explicitHit = TargetTrack("tgt1", isExplicit: true);
    var cleanVersion = TargetTrack("tgt1-clean", isExplicit: false);

    _target.Setup(x => x.FindCleanVersionAsync(UserId, explicitHit, It.IsAny<CancellationToken>()))
        .ReturnsAsync(cleanVersion);

    var match = await _matcher.MatchAsync(UserId, _target.Object, source,
        Index(("ISRC1", explicitHit)), preferClean: true, CancellationToken.None);

    Assert.Same(cleanVersion, match.Target);
    Assert.Equal(MatchMethods.IsrcClean, match.Method);
  }

  [Fact]
  public async Task MatchAsync_ExplicitIsrcHitWithCleanToggleAndNoCleanVersion_IsUnmatched()
  {
    // Mirrors same-service clean semantics: no clean version means the track is dropped,
    // not silently copied explicit.
    var source = Source(isrc: "ISRC1", isExplicit: true);
    var explicitHit = TargetTrack("tgt1", isExplicit: true);

    _target.Setup(x => x.FindCleanVersionAsync(UserId, explicitHit, It.IsAny<CancellationToken>()))
        .ReturnsAsync((MusicTrack?)null);

    var match = await _matcher.MatchAsync(UserId, _target.Object, source,
        Index(("ISRC1", explicitHit)), preferClean: true, CancellationToken.None);

    Assert.Null(match.Target);
    Assert.Equal(MatchMethods.None, match.Method);
  }

  [Fact]
  public async Task MatchAsync_NoIsrc_SearchFallbackMatchesOnNameArtistDuration()
  {
    var source = Source(name: "My Song", isExplicit: false, durationMs: 200_000);
    _target.Setup(x => x.SearchTracksAsync(UserId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<MusicTrack>
        {
          TargetTrack("wrong-name", name: "Different Song"),
          TargetTrack("wrong-artist", name: "My Song", artist: "Someone Else"),
          TargetTrack("wrong-duration", name: "My Song", durationMs: 150_000),
          TargetTrack("right", name: "My Song", durationMs: 201_000)
        });

    var match = await _matcher.MatchAsync(UserId, _target.Object, source,
        Index(), preferClean: false, CancellationToken.None);

    Assert.Equal("right", match.Target?.Id);
    Assert.Equal(MatchMethods.Search, match.Method);
  }

  [Fact]
  public async Task MatchAsync_SearchFallback_NormalizesCleanSuffixAndJoinedArtists()
  {
    // Cross-provider: Spotify source artists vs Apple's joined artist string, and a
    // "(Clean)"-suffixed target title.
    var source = new MusicTrack("src1", "Duet", true,
        new List<MusicArtist> { new("Artist B") });
    _target.Setup(x => x.SearchTracksAsync(UserId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<MusicTrack>
        {
          new("clean-ver", "Duet (Clean)", false, new List<MusicArtist> { new("Artist A & Artist B") })
        });

    var match = await _matcher.MatchAsync(UserId, _target.Object, source,
        Index(), preferClean: true, CancellationToken.None);

    Assert.Equal("clean-ver", match.Target?.Id);
    Assert.Equal(MatchMethods.SearchClean, match.Method);
  }

  [Fact]
  public async Task MatchAsync_ExplicitSourceWithCleanToggle_SearchWithOnlyExplicitResults_IsUnmatched()
  {
    var source = Source(isExplicit: true);
    _target.Setup(x => x.SearchTracksAsync(UserId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<MusicTrack> { TargetTrack("explicit-only", isExplicit: true) });

    var match = await _matcher.MatchAsync(UserId, _target.Object, source,
        Index(), preferClean: true, CancellationToken.None);

    Assert.Null(match.Target);
    Assert.Equal(MatchMethods.None, match.Method);
  }

  [Fact]
  public async Task MatchAsync_FaithfulCopy_PrefersSameExplicitnessThenAnyPlausible()
  {
    var source = Source(isExplicit: true);
    _target.Setup(x => x.SearchTracksAsync(UserId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<MusicTrack>
        {
          TargetTrack("clean-first", isExplicit: false),
          TargetTrack("explicit-later", isExplicit: true)
        });

    var match = await _matcher.MatchAsync(UserId, _target.Object, source,
        Index(), preferClean: false, CancellationToken.None);

    Assert.Equal("explicit-later", match.Target?.Id);
    Assert.Equal(MatchMethods.Search, match.Method);
  }

  [Fact]
  public async Task MatchAsync_NothingFound_ReturnsNone()
  {
    var source = Source();
    _target.Setup(x => x.SearchTracksAsync(UserId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<MusicTrack>());

    var match = await _matcher.MatchAsync(UserId, _target.Object, source,
        Index(), preferClean: false, CancellationToken.None);

    Assert.Null(match.Target);
    Assert.Equal(MatchMethods.None, match.Method);
  }

  [Fact]
  public async Task PrefetchByIsrcAsync_DelegatesToTargetService()
  {
    var isrcs = new[] { "A", "B" };
    var expected = Index(("A", TargetTrack("t1")));
    _target.Setup(x => x.GetTracksByIsrcAsync(UserId, isrcs, It.IsAny<CancellationToken>()))
        .ReturnsAsync(expected);

    var index = await _matcher.PrefetchByIsrcAsync(UserId, _target.Object, isrcs, CancellationToken.None);

    Assert.Same(expected, index);
  }
}
