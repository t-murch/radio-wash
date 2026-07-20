using Moq;
using RadioWash.Api.Models.AppleMusic;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Tests for the AppleMusicMusicService adapter that wraps IAppleMusicService behind the
/// provider-agnostic IMusicService contract. Coverage mirrors SpotifyMusicServiceTests:
/// mapping between Apple-shaped DTOs and generic records, the library→catalog id
/// resolution that keeps cleaners cross-catalog safe, contentRating→IsExplicit mapping,
/// clean-version selection heuristics, and the raw-catalog-id passthrough on AddTracks.
/// </summary>
public class AppleMusicMusicServiceTests
{
  private const int UserId = 7;

  private readonly Mock<IAppleMusicService> _appleMusic = new();
  private readonly AppleMusicMusicService _adapter;

  public AppleMusicMusicServiceTests()
  {
    _adapter = new AppleMusicMusicService(_appleMusic.Object);
  }

  private static AppleLibrarySong CreateLibrarySong(
    string libraryId, string name, string? catalogId = null, string? contentRating = null,
    string artistName = "Test Artist", string? catalogRelationshipId = null)
  {
    return new AppleLibrarySong
    {
      Id = libraryId,
      Attributes = new AppleLibrarySongAttributes
      {
        Name = name,
        ArtistName = artistName,
        AlbumName = "Test Album",
        ContentRating = contentRating,
        DurationInMillis = 200_000,
        PlayParams = catalogId is null ? null : new ApplePlayParams { Id = libraryId, CatalogId = catalogId }
      },
      Relationships = catalogRelationshipId is null ? null : new AppleLibrarySongRelationships
      {
        Catalog = new AppleRelationship
        {
          Data = new[] { new AppleResourceRef { Id = catalogRelationshipId, Type = "songs" } }
        }
      }
    };
  }

  private static AppleCatalogSong CreateCatalogSong(
    string id, string name, string? contentRating = null, string artistName = "Test Artist")
  {
    return new AppleCatalogSong
    {
      Id = id,
      Attributes = new AppleCatalogSongAttributes
      {
        Name = name,
        ArtistName = artistName,
        AlbumName = "Test Album",
        ContentRating = contentRating,
        DurationInMillis = 200_000
      }
    };
  }

  [Fact]
  public void ProviderName_IsAppleMusic()
  {
    Assert.Equal("apple_music", _adapter.ProviderName);
  }

  [Fact]
  public async Task GetUserProfileAsync_ReturnsStorefrontScopedPlaceholder()
  {
    // Apple Music's API exposes no user identity; the adapter fabricates a stable profile.
    _appleMusic.Setup(x => x.GetUserStorefrontAsync(UserId, It.IsAny<CancellationToken>()))
        .ReturnsAsync("gb");

    var profile = await _adapter.GetUserProfileAsync(UserId, CancellationToken.None);

    Assert.Equal("apple_music:gb", profile.Id);
    Assert.NotNull(profile.DisplayName);
  }

  [Fact]
  public async Task GetUserPlaylistsAsync_MapsAttributesAndSizesArtwork()
  {
    _appleMusic.Setup(x => x.GetUserPlaylistsAsync(UserId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[]
        {
          new AppleLibraryPlaylist
          {
            Id = "p.abc",
            Attributes = new AppleLibraryPlaylistAttributes
            {
              Name = "My Mix",
              Description = new AppleDescription { Standard = "desc" },
              Artwork = new AppleArtwork { Url = "https://art.example/{w}x{h}bb.jpg" },
              CanEdit = true
            }
          }
        });

    var playlists = await _adapter.GetUserPlaylistsAsync(UserId, CancellationToken.None);

    var playlist = Assert.Single(playlists);
    Assert.Equal("p.abc", playlist.Id);
    Assert.Equal("My Mix", playlist.Name);
    Assert.Equal("desc", playlist.Description);
    Assert.Equal("https://art.example/300x300bb.jpg", playlist.ImageUrl);
  }

  [Fact]
  public async Task GetPlaylistTracksAsync_ResolvesCatalogIdsAndContentRating()
  {
    _appleMusic.Setup(x => x.GetPlaylistTracksAsync(UserId, "p.abc", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[]
        {
          CreateLibrarySong("i.1", "Explicit Song", catalogId: "cat100", contentRating: "explicit"),
          CreateLibrarySong("i.2", "Clean Song", catalogId: "cat200", contentRating: "clean"),
          CreateLibrarySong("i.3", "Unrated Song", catalogRelationshipId: "cat300"),
          CreateLibrarySong("i.4", "Personal Upload")
        });

    var tracks = await _adapter.GetPlaylistTracksAsync(UserId, "p.abc", CancellationToken.None);

    Assert.Equal(4, tracks.Count);
    // playParams.catalogId wins
    Assert.Equal("cat100", tracks[0].Id);
    Assert.True(tracks[0].IsExplicit);
    Assert.Equal("cat200", tracks[1].Id);
    Assert.False(tracks[1].IsExplicit);
    // catalog relationship is the fallback source
    Assert.Equal("cat300", tracks[2].Id);
    Assert.False(tracks[2].IsExplicit);
    // no catalog linkage → library id passes through (unmatchable downstream, never an error)
    Assert.Equal("i.4", tracks[3].Id);
  }

  [Fact]
  public async Task FindCleanVersionAsync_WithNonExplicitTrack_ReturnsSameTrackWithoutSearching()
  {
    var track = new MusicTrack("cat1", "Already Clean", false, new List<MusicArtist> { new("Artist") });

    var result = await _adapter.FindCleanVersionAsync(UserId, track, CancellationToken.None);

    Assert.Same(track, result);
    _appleMusic.Verify(
      x => x.SearchCatalogSongsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
      Times.Never);
  }

  [Fact]
  public async Task FindCleanVersionAsync_PicksNonExplicitCandidateWithMatchingNameAndArtist()
  {
    var explicitTrack = new MusicTrack("cat1", "Bad Song", true, new List<MusicArtist> { new("Test Artist") });

    _appleMusic.Setup(x => x.SearchCatalogSongsAsync(UserId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[]
        {
          CreateCatalogSong("cat2", "Bad Song", contentRating: "explicit"),
          CreateCatalogSong("cat3", "Bad Song", contentRating: "explicit", artistName: "Test Artist"),
          CreateCatalogSong("cat4", "Bad Song (Clean)", contentRating: "clean", artistName: "Test Artist"),
        });

    var result = await _adapter.FindCleanVersionAsync(UserId, explicitTrack, CancellationToken.None);

    Assert.NotNull(result);
    Assert.Equal("cat4", result.Id);
    Assert.False(result.IsExplicit);
  }

  [Fact]
  public async Task FindCleanVersionAsync_RejectsCandidatesWithDifferentArtist()
  {
    var explicitTrack = new MusicTrack("cat1", "Bad Song", true, new List<MusicArtist> { new("Original Artist") });

    _appleMusic.Setup(x => x.SearchCatalogSongsAsync(UserId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[]
        {
          // Clean rating and matching name, but a cover by someone else.
          CreateCatalogSong("cat9", "Bad Song", contentRating: "clean", artistName: "Karaoke Band")
        });

    var result = await _adapter.FindCleanVersionAsync(UserId, explicitTrack, CancellationToken.None);

    Assert.Null(result);
  }

  [Fact]
  public async Task FindCleanVersionAsync_MatchesMultiArtistJoinedNames()
  {
    // Apple joins artists into one string; a source artist must match within it.
    var explicitTrack = new MusicTrack("cat1", "Duet", true, new List<MusicArtist> { new("Artist B") });

    _appleMusic.Setup(x => x.SearchCatalogSongsAsync(UserId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[]
        {
          CreateCatalogSong("cat5", "Duet", contentRating: "clean", artistName: "Artist A & Artist B")
        });

    var result = await _adapter.FindCleanVersionAsync(UserId, explicitTrack, CancellationToken.None);

    Assert.NotNull(result);
    Assert.Equal("cat5", result.Id);
  }

  [Fact]
  public async Task CreatePlaylistAsync_MapsCreatedLibraryPlaylist()
  {
    _appleMusic.Setup(x => x.CreateLibraryPlaylistAsync(UserId, "Clean Mix", "desc", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AppleLibraryPlaylist
        {
          Id = "p.new",
          Attributes = new AppleLibraryPlaylistAttributes { Name = "Clean Mix", CanEdit = true }
        });

    var playlist = await _adapter.CreatePlaylistAsync(UserId, "Clean Mix", "desc", CancellationToken.None);

    Assert.Equal("p.new", playlist.Id);
    Assert.Equal("Clean Mix", playlist.Name);
  }

  [Fact]
  public async Task AddTracksToPlaylistAsync_PassesRawCatalogIdsThrough()
  {
    IEnumerable<string>? captured = null;
    _appleMusic.Setup(x => x.AddTracksToLibraryPlaylistAsync(UserId, "p.1", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
        .Callback<int, string, IEnumerable<string>, CancellationToken>((_, _, ids, _) => captured = ids)
        .Returns(Task.CompletedTask);

    await _adapter.AddTracksToPlaylistAsync(UserId, "p.1", new[] { "cat1", "cat2" }, CancellationToken.None);

    Assert.NotNull(captured);
    Assert.Equal(new[] { "cat1", "cat2" }, captured);
  }
}
