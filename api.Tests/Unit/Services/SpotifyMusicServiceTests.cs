using Moq;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Tests for the SpotifyMusicService adapter that wraps the existing ISpotifyService behind
/// the provider-agnostic IMusicService contract. Coverage focuses on the mapping between
/// Spotify-shaped DTOs and the generic MusicTrack/PlaylistSummary records and — critically —
/// the Spotify-specific <c>spotify:track:&lt;id&gt;</c> URI format that was previously
/// constructed in the playlist cleaner and now lives in the adapter.
/// </summary>
public class SpotifyMusicServiceTests
{
  private readonly Mock<ISpotifyService> _spotify = new();
  private readonly SpotifyMusicService _adapter;

  public SpotifyMusicServiceTests()
  {
    _adapter = new SpotifyMusicService(_spotify.Object);
  }

  [Fact]
  public void ProviderName_IsSpotify()
  {
    Assert.Equal("spotify", _adapter.ProviderName);
  }

  [Fact]
  public async Task GetUserProfileAsync_MapsSpotifyUserProfileFields()
  {
    _spotify.Setup(x => x.GetUserProfileAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new SpotifyUserProfile
    {
      Id = "spotify_user_42",
      DisplayName = "Ada",
      Email = "ada@example.com"
    });

    var profile = await _adapter.GetUserProfileAsync(7, CancellationToken.None);

    Assert.Equal("spotify_user_42", profile.Id);
    Assert.Equal("Ada", profile.DisplayName);
    Assert.Equal("ada@example.com", profile.Email);
  }

  [Fact]
  public async Task GetUserPlaylistsAsync_ProjectsPlaylistDtoIntoPlaylistSummary()
  {
    _spotify.Setup(x => x.GetUserPlaylistsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
    {
      new PlaylistDto
      {
        Id = "p1",
        Name = "My Jams",
        Description = "desc",
        ImageUrl = "http://img/1",
        TrackCount = 42,
        OwnerId = "o1",
        OwnerName = "Ada"
      }
    });

    var result = await _adapter.GetUserPlaylistsAsync(7, CancellationToken.None);

    var p = Assert.Single(result);
    Assert.Equal("p1", p.Id);
    Assert.Equal("My Jams", p.Name);
    Assert.Equal("desc", p.Description);
    Assert.Equal("http://img/1", p.ImageUrl);
    Assert.Equal(42, p.TrackCount);
    Assert.Equal("o1", p.OwnerId);
    Assert.Equal("Ada", p.OwnerName);
  }

  [Fact]
  public async Task GetPlaylistTracksAsync_MapsSpotifyTracksIncludingExplicitAndArtists()
  {
    _spotify.Setup(x => x.GetPlaylistTracksAsync(7, "pl-abc", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
    {
      new SpotifyTrack
      {
        Id = "t1",
        Name = "Song",
        Explicit = true,
        Artists = new[]
        {
          new SpotifyArtist { Id = "a1", Name = "Artist A" },
          new SpotifyArtist { Id = "a2", Name = "Artist B" }
        },
        Album = new SpotifyAlbum { Id = "al", Name = "Album" },
        Uri = "spotify:track:t1"
      }
    });

    var tracks = await _adapter.GetPlaylistTracksAsync(7, "pl-abc", CancellationToken.None);

    var t = Assert.Single(tracks);
    Assert.Equal("t1", t.Id);
    Assert.Equal("Song", t.Name);
    Assert.True(t.IsExplicit);
    Assert.Collection(t.Artists,
      a => Assert.Equal("Artist A", a.Name),
      a => Assert.Equal("Artist B", a.Name));
  }

  [Fact]
  public async Task FindCleanVersionAsync_OnExplicitTrackWithMatch_ReturnsMappedCleanVersion()
  {
    SpotifyTrack? observedInput = null;
    _spotify.Setup(x => x.FindCleanVersionAsync(7, It.IsAny<SpotifyTrack>(), It.IsAny<CancellationToken>()))
      .Callback<int, SpotifyTrack, CancellationToken>((_, t, _) => observedInput = t)
      .ReturnsAsync(new SpotifyTrack
      {
        Id = "clean-t1",
        Name = "Song",
        Explicit = false,
        Artists = new[] { new SpotifyArtist { Id = "a1", Name = "Artist A" } },
        Album = new SpotifyAlbum { Id = "al", Name = "Album" },
        Uri = "spotify:track:clean-t1"
      });

    var explicitTrack = new MusicTrack(
      Id: "t1",
      Name: "Song",
      IsExplicit: true,
      Artists: new[] { new MusicArtist("Artist A") });

    var clean = await _adapter.FindCleanVersionAsync(7, explicitTrack, CancellationToken.None);

    Assert.NotNull(clean);
    Assert.Equal("clean-t1", clean!.Id);
    Assert.False(clean.IsExplicit);

    // The adapter must reconstruct a SpotifyTrack that carries enough fields for
    // ISpotifyService.FindCleanVersionAsync to build its search query.
    Assert.NotNull(observedInput);
    Assert.Equal("t1", observedInput!.Id);
    Assert.Equal("Song", observedInput.Name);
    Assert.True(observedInput.Explicit);
    Assert.Equal("Artist A", observedInput.Artists[0].Name);
  }

  [Fact]
  public async Task FindCleanVersionAsync_WhenUpstreamReturnsNull_ReturnsNull()
  {
    _spotify.Setup(x => x.FindCleanVersionAsync(7, It.IsAny<SpotifyTrack>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((SpotifyTrack?)null);

    var explicitTrack = new MusicTrack("t1", "Song", IsExplicit: true,
      new[] { new MusicArtist("Artist A") });

    var clean = await _adapter.FindCleanVersionAsync(7, explicitTrack, CancellationToken.None);

    Assert.Null(clean);
  }

  [Fact]
  public async Task CreatePlaylistAsync_MapsSpotifyPlaylistIntoSummary()
  {
    _spotify.Setup(x => x.CreatePlaylistAsync(7, "Clean - My Jams", "desc", It.IsAny<CancellationToken>()))
      .ReturnsAsync(new SpotifyPlaylist
      {
        Id = "new-pl",
        Name = "Clean - My Jams",
        Description = "desc",
        Images = new[] { new SpotifyImage { Url = "http://img/new" } },
        Tracks = new SpotifyPlaylistTracksRef { Total = 0, Href = "href" },
        Owner = new SpotifyUser { Id = "o1", DisplayName = "Ada" }
      });

    var summary = await _adapter.CreatePlaylistAsync(7, "Clean - My Jams", "desc", CancellationToken.None);

    Assert.Equal("new-pl", summary.Id);
    Assert.Equal("Clean - My Jams", summary.Name);
    Assert.Equal("desc", summary.Description);
    Assert.Equal("http://img/new", summary.ImageUrl);
    Assert.Equal(0, summary.TrackCount);
    Assert.Equal("o1", summary.OwnerId);
    Assert.Equal("Ada", summary.OwnerName);
  }

  [Fact]
  public async Task AddTracksToPlaylistAsync_WrapsRawIdsInSpotifyUriFormat()
  {
    // This is the behavior that moved from the playlist cleaner into the adapter. Callers
    // pass raw IDs; the adapter owns the spotify:track:<id> transformation.
    IEnumerable<string>? observed = null;
    _spotify.Setup(x => x.AddTracksToPlaylistAsync(7, "pl", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
      .Callback<int, string, IEnumerable<string>, CancellationToken>((_, _, uris, _) => observed = uris.ToList())
      .Returns(Task.CompletedTask);

    await _adapter.AddTracksToPlaylistAsync(7, "pl", new[] { "abc", "def" }, CancellationToken.None);

    Assert.NotNull(observed);
    Assert.Equal(new[] { "spotify:track:abc", "spotify:track:def" }, observed);
  }

  [Fact]
  public async Task AddTracksToPlaylistAsync_WithEmptyInput_ForwardsEmptyList()
  {
    IEnumerable<string>? observed = null;
    _spotify.Setup(x => x.AddTracksToPlaylistAsync(7, "pl", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
      .Callback<int, string, IEnumerable<string>, CancellationToken>((_, _, uris, _) => observed = uris.ToList())
      .Returns(Task.CompletedTask);

    await _adapter.AddTracksToPlaylistAsync(7, "pl", Array.Empty<string>(), CancellationToken.None);

    Assert.NotNull(observed);
    Assert.Empty(observed!);
  }
}
