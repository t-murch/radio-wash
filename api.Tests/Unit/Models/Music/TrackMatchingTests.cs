using RadioWash.Api.Models.Music;

namespace RadioWash.Api.Tests.Unit.Models.Music;

/// <summary>
/// Pure-function tests for the shared matching heuristics. The null and blank cases pin the
/// fix for a production NullReferenceException: Apple omits artistName for untagged library
/// uploads, and the overlap check used to dereference it.
/// </summary>
public class TrackMatchingTests
{
  private static IReadOnlyList<MusicArtist> Artists(params string?[] names) =>
    names.Select(n => new MusicArtist(n!)).ToList();

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void HasArtistOverlap_JoinedString_BlankCandidate_NeverMatches(string? candidate)
  {
    Assert.False(TrackMatching.HasArtistOverlap(Artists("Artist"), candidate));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void HasArtistOverlap_JoinedString_BlankSource_NeverMatchesAndDoesNotThrow(string? source)
  {
    Assert.False(TrackMatching.HasArtistOverlap(Artists(source), "Artist"));
  }

  [Fact]
  public void HasArtistOverlap_Lists_BlankOnEitherSide_NeverMatches()
  {
    Assert.False(TrackMatching.HasArtistOverlap(Artists((string?)null), Artists("Artist")));
    Assert.False(TrackMatching.HasArtistOverlap(Artists("Artist"), Artists((string?)null)));
    Assert.False(TrackMatching.HasArtistOverlap(Artists(""), Artists("Artist")));
    Assert.False(TrackMatching.HasArtistOverlap(Artists("Artist"), Artists("")));
  }

  [Fact]
  public void HasArtistOverlap_EmptyCandidateString_DoesNotMatchEverything()
  {
    // "x".Contains("") is true, so without the blank guard an artist-less candidate would
    // match any source.
    Assert.False(TrackMatching.HasArtistOverlap(Artists("Kendrick Lamar"), ""));
  }

  [Fact]
  public void HasArtistOverlap_EmptySourceList_NeverMatches()
  {
    Assert.False(TrackMatching.HasArtistOverlap(Array.Empty<MusicArtist>(), "Artist"));
    Assert.False(TrackMatching.HasArtistOverlap(Array.Empty<MusicArtist>(), Artists("Artist")));
  }

  [Fact]
  public void HasArtistOverlap_ContainmentInEitherDirection_Matches()
  {
    Assert.True(TrackMatching.HasArtistOverlap(Artists("Artist B"), "Artist A & Artist B"));
    Assert.True(TrackMatching.HasArtistOverlap(Artists("Artist A & Artist B"), "artist b"));
  }

  [Fact]
  public void NamesMatch_NullTitles_DoNotThrow()
  {
    Assert.False(TrackMatching.NamesMatch(null, "Song"));
    Assert.False(TrackMatching.NamesMatch("Song", null));
    Assert.True(TrackMatching.NamesMatch(null, null));
    Assert.True(TrackMatching.NamesMatch(null, ""));
  }

  [Fact]
  public void NamesMatch_IgnoresCaseAndCleanSuffix()
  {
    Assert.True(TrackMatching.NamesMatch("Song (Clean)", "song"));
    Assert.True(TrackMatching.NamesMatch("Song [Clean]", "SONG"));
    Assert.False(TrackMatching.NamesMatch("Song (Live)", "Song"));
  }
}
