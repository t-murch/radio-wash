using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RadioWash.Api.Configuration;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RadioWash.Api.Test.UnitTests;

public class SpotifyServiceTests : IDisposable
{
  private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
  private readonly Mock<IMusicTokenService> _musicTokenServiceMock;
  private readonly Mock<ILogger<SpotifyService>> _loggerMock;
  private readonly HttpClient _httpClient;
  private readonly SpotifyService _spotifyService;
  private readonly SpotifySettings _spotifySettings;

  public SpotifyServiceTests()
  {
    _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
    _musicTokenServiceMock = new Mock<IMusicTokenService>();
    _loggerMock = new Mock<ILogger<SpotifyService>>();

    _httpClient = new HttpClient(_httpMessageHandlerMock.Object);

    _spotifySettings = new SpotifySettings
    {
      ClientId = "test-client-id",
      ClientSecret = "test-client-secret",
      RedirectUri = "https://test.com/callback",
      Scopes = new[] { "playlist-read-private", "playlist-modify-private" }
    };

    var spotifySettingsOptions = Options.Create(_spotifySettings);

    _spotifyService = new SpotifyService(
        _httpClient,
        spotifySettingsOptions,
        _musicTokenServiceMock.Object,
        _loggerMock.Object);

    // Default setup for token service
    _musicTokenServiceMock
        .Setup(m => m.GetValidAccessTokenAsync(It.IsAny<int>(), "spotify"))
        .ReturnsAsync("test-access-token");
  }

  private void SetupHttpResponse(HttpStatusCode statusCode, string content = "")
  {
    _httpMessageHandlerMock
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage
        {
          StatusCode = statusCode,
          Content = new StringContent(content, Encoding.UTF8, "application/json")
        });
  }

  private void SetupHttpSequence(params (HttpStatusCode StatusCode, string Content)[] responses)
  {
    var setupSequence = _httpMessageHandlerMock
        .Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());

    foreach (var (statusCode, content) in responses)
    {
      setupSequence = setupSequence.ReturnsAsync(new HttpResponseMessage
      {
        StatusCode = statusCode,
        Content = new StringContent(content, Encoding.UTF8, "application/json")
      });
    }
  }

  [Fact]
  public async Task GetUserProfileAsync_WhenSuccessful_ShouldReturnUserProfile()
  {
    // Arrange
    var userId = 1;
    var expectedProfile = new SpotifyUserProfile
    {
      Id = "spotify-user-123",
      DisplayName = "Test User",
      Email = "test@test.com"
    };

    var jsonResponse = JsonSerializer.Serialize(expectedProfile);
    SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

    // Act
    var result = await _spotifyService.GetUserProfileAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("spotify-user-123", result.Id);
    Assert.Equal("Test User", result.DisplayName);
    Assert.Equal("test@test.com", result.Email);

    _musicTokenServiceMock.Verify(m => m.GetValidAccessTokenAsync(userId, "spotify"), Times.Once);
  }

  [Fact]
  public async Task GetUserProfileAsync_WhenUnauthorized_ShouldRefreshTokenAndRetry()
  {
    // Arrange
    var userId = 1;
    var expectedProfile = new SpotifyUserProfile
    {
      Id = "spotify-user-123",
      DisplayName = "Test User",
      Email = "test@test.com"
    };

    var jsonResponse = JsonSerializer.Serialize(expectedProfile);

    // Setup sequential responses: first 401, then success
    var callCount = 0;
    _httpMessageHandlerMock
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Returns<HttpRequestMessage, CancellationToken>((request, token) =>
        {
          callCount++;
          if (callCount == 1)
          {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
          }
          return Task.FromResult(new HttpResponseMessage
          {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
          });
        });

    _musicTokenServiceMock
        .Setup(m => m.RefreshTokensAsync(userId, "spotify"))
        .ReturnsAsync(true);

    _musicTokenServiceMock
        .SetupSequence(m => m.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync("old-token")
        .ReturnsAsync("new-token");

    // Act
    var result = await _spotifyService.GetUserProfileAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("spotify-user-123", result.Id);

    _musicTokenServiceMock.Verify(m => m.RefreshTokensAsync(userId, "spotify"), Times.Once);
    _musicTokenServiceMock.Verify(m => m.GetValidAccessTokenAsync(userId, "spotify"), Times.Exactly(2));
    
    // Verify two HTTP calls were made (first 401, second success)
    _httpMessageHandlerMock
        .Protected()
        .Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task GetUserPlaylistsAsync_WhenSuccessful_ShouldReturnPlaylists()
  {
    // Arrange
    var userId = 1;
    var spotifyPlaylists = new SpotifyPlaylistsResponse
    {
      Items = new[]
      {
        new SpotifyPlaylist
        {
          Id = "playlist-1",
          Name = "My Mix 1",
          Description = "Great songs",
          Images = new[] { new SpotifyImage { Url = "https://test.com/image1.jpg" } },
          Tracks = new SpotifyPlaylistTracksRef { Total = 20 },
          Owner = new SpotifyUser { Id = "user-123", DisplayName = "Test User" }
        },
        new SpotifyPlaylist
        {
          Id = "playlist-2", 
          Name = "My Mix 2",
          Description = null,
          Images = null,
          Tracks = new SpotifyPlaylistTracksRef { Total = 15 },
          Owner = new SpotifyUser { Id = "user-123", DisplayName = "Test User" }
        }
      },
      Next = null
    };

    var jsonResponse = JsonSerializer.Serialize(spotifyPlaylists);
    SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

    // Act
    var result = await _spotifyService.GetUserPlaylistsAsync(userId);

    // Assert
    var playlists = result.ToList();
    Assert.Equal(2, playlists.Count);

    Assert.Equal("playlist-1", playlists[0].Id);
    Assert.Equal("My Mix 1", playlists[0].Name);
    Assert.Equal("Great songs", playlists[0].Description);
    Assert.Equal("https://test.com/image1.jpg", playlists[0].ImageUrl);
    Assert.Equal(20, playlists[0].TrackCount);
    Assert.Equal("user-123", playlists[0].OwnerId);

    Assert.Equal("playlist-2", playlists[1].Id);
    Assert.Equal("My Mix 2", playlists[1].Name);
    Assert.Null(playlists[1].Description);
    Assert.Null(playlists[1].ImageUrl);
    Assert.Equal(15, playlists[1].TrackCount);
  }

  [Fact]
  public async Task GetUserPlaylistsAsync_WithPagination_ShouldReturnAllPlaylists()
  {
    // Arrange
    var userId = 1;

    // First page
    var firstPageResponse = new SpotifyPlaylistsResponse
    {
      Items = new[]
      {
        new SpotifyPlaylist
        {
          Id = "playlist-1",
          Name = "My Mix 1",
          Tracks = new SpotifyPlaylistTracksRef { Total = 10 },
          Owner = new SpotifyUser { Id = "user-123" }
        }
      },
      Next = "https://api.spotify.com/v1/me/playlists?offset=50&limit=50"
    };

    // Second page
    var secondPageResponse = new SpotifyPlaylistsResponse
    {
      Items = new[]
      {
        new SpotifyPlaylist
        {
          Id = "playlist-2",
          Name = "My Mix 2", 
          Tracks = new SpotifyPlaylistTracksRef { Total = 15 },
          Owner = new SpotifyUser { Id = "user-123" }
        }
      },
      Next = null
    };

    SetupHttpSequence(
        (HttpStatusCode.OK, JsonSerializer.Serialize(firstPageResponse)),
        (HttpStatusCode.OK, JsonSerializer.Serialize(secondPageResponse))
    );

    // Act
    var result = await _spotifyService.GetUserPlaylistsAsync(userId);

    // Assert
    var playlists = result.ToList();
    Assert.Equal(2, playlists.Count);
    Assert.Equal("playlist-1", playlists[0].Id);
    Assert.Equal("playlist-2", playlists[1].Id);
  }

  [Fact]
  public async Task GetPlaylistTracksAsync_WhenSuccessful_ShouldReturnTracks()
  {
    // Arrange
    var userId = 1;
    var playlistId = "playlist-123";

    var tracksResponse = new SpotifyPlaylistTracksResponse
    {
      Items = new[]
      {
        new SpotifyPlaylistTrack
        {
          Track = new SpotifyTrack
          {
            Id = "track-1",
            Name = "Song 1",
            Explicit = true,
            Uri = "spotify:track:track-1",
            Artists = new[] { new SpotifyArtist { Id = "artist-1", Name = "Artist 1" } },
            Album = new SpotifyAlbum { Id = "album-1", Name = "Album 1" }
          },
          AddedAt = "2023-01-01T00:00:00Z"
        },
        new SpotifyPlaylistTrack
        {
          Track = new SpotifyTrack
          {
            Id = "track-2",
            Name = "Song 2", 
            Explicit = false,
            Uri = "spotify:track:track-2",
            Artists = new[] { new SpotifyArtist { Id = "artist-2", Name = "Artist 2" } },
            Album = new SpotifyAlbum { Id = "album-2", Name = "Album 2" }
          },
          AddedAt = "2023-01-02T00:00:00Z"
        }
      },
      Next = null
    };

    var jsonResponse = JsonSerializer.Serialize(tracksResponse);
    SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

    // Act
    var result = await _spotifyService.GetPlaylistTracksAsync(userId, playlistId);

    // Assert
    var tracks = result.ToList();
    Assert.Equal(2, tracks.Count);

    Assert.Equal("track-1", tracks[0].Id);
    Assert.Equal("Song 1", tracks[0].Name);
    Assert.True(tracks[0].Explicit);
    Assert.Equal("Artist 1", tracks[0].Artists[0].Name);

    Assert.Equal("track-2", tracks[1].Id);
    Assert.Equal("Song 2", tracks[1].Name);
    Assert.False(tracks[1].Explicit);
    Assert.Equal("Artist 2", tracks[1].Artists[0].Name);
  }

  [Fact]
  public async Task CreatePlaylistAsync_WhenSuccessful_ShouldReturnCreatedPlaylist()
  {
    // Arrange
    var userId = 1;
    var playlistName = "Test Playlist";
    var description = "Test Description";

    var userProfile = new SpotifyUserProfile { Id = "spotify-user-123" };
    var createdPlaylist = new SpotifyPlaylist
    {
      Id = "new-playlist-123",
      Name = playlistName,
      Description = description,
      Owner = new SpotifyUser { Id = "spotify-user-123" },
      Tracks = new SpotifyPlaylistTracksRef { Total = 0 }
    };

    SetupHttpSequence(
        (HttpStatusCode.OK, JsonSerializer.Serialize(userProfile)), // GetUserProfileAsync
        (HttpStatusCode.Created, JsonSerializer.Serialize(createdPlaylist)) // CreatePlaylist
    );

    // Act
    var result = await _spotifyService.CreatePlaylistAsync(userId, playlistName, description);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("new-playlist-123", result.Id);
    Assert.Equal(playlistName, result.Name);
    Assert.Equal(description, result.Description);
  }

  [Fact]
  public async Task AddTracksToPlaylistAsync_WithEmptyUris_ShouldNotCallApi()
  {
    // Arrange
    var userId = 1;
    var playlistId = "playlist-123";
    var emptyUris = new List<string>();

    // Act
    await _spotifyService.AddTracksToPlaylistAsync(userId, playlistId, emptyUris);

    // Assert
    _httpMessageHandlerMock
        .Protected()
        .Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task AddTracksToPlaylistAsync_WithUris_ShouldCallApiInChunks()
  {
    // Arrange
    var userId = 1;
    var playlistId = "playlist-123";
    
    // Create 150 URIs to test chunking (should be split into 2 chunks of 100 and 50)
    var trackUris = Enumerable.Range(1, 150)
        .Select(i => $"spotify:track:track-{i}")
        .ToList();

    SetupHttpResponse(HttpStatusCode.OK, "{}");

    // Act
    await _spotifyService.AddTracksToPlaylistAsync(userId, playlistId, trackUris);

    // Assert - Should make 2 API calls for 150 tracks (100 + 50)
    _httpMessageHandlerMock
        .Protected()
        .Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task FindCleanVersionAsync_WhenTrackAlreadyClean_ShouldReturnSameTrack()
  {
    // Arrange
    var userId = 1;
    var cleanTrack = new SpotifyTrack
    {
      Id = "track-1",
      Name = "Song 1",
      Explicit = false, // Already clean
      Artists = new[] { new SpotifyArtist { Name = "Artist 1" } },
      Album = new SpotifyAlbum { Id = "album-1", Name = "Album 1" },
      Uri = "spotify:track:track-1"
    };

    // Act
    var result = await _spotifyService.FindCleanVersionAsync(userId, cleanTrack);

    // Assert
    Assert.Equal(cleanTrack, result);

    // Should not make any HTTP calls since track is already clean
    _httpMessageHandlerMock
        .Protected()
        .Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task FindCleanVersionAsync_WhenCleanVersionFound_ShouldReturnCleanTrack()
  {
    // Arrange
    var userId = 1;
    var explicitTrack = new SpotifyTrack
    {
      Id = "track-1",
      Name = "Song 1",
      Explicit = true,
      Artists = new[] { new SpotifyArtist { Name = "Artist 1" } },
      Album = new SpotifyAlbum { Id = "album-1", Name = "Album 1" },
      Uri = "spotify:track:track-1"
    };

    var cleanTrack = new SpotifyTrack
    {
      Id = "track-1-clean",
      Name = "Song 1",
      Explicit = false,
      Artists = new[] { new SpotifyArtist { Name = "Artist 1" } },
      Album = new SpotifyAlbum { Id = "album-1", Name = "Album 1" },
      Uri = "spotify:track:track-1-clean"
    };

    var searchResponse = new SpotifySearchResponse
    {
      Tracks = new SpotifyTracks
      {
        Items = new[] { cleanTrack },
        Total = 1
      }
    };

    SetupHttpResponse(HttpStatusCode.OK, JsonSerializer.Serialize(searchResponse));

    // Act
    var result = await _spotifyService.FindCleanVersionAsync(userId, explicitTrack);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("track-1-clean", result.Id);
    Assert.False(result.Explicit);
    Assert.Equal("Song 1", result.Name);
  }

  [Fact]
  public async Task FindCleanVersionAsync_WhenNoCleanVersionFound_ShouldReturnNull()
  {
    // Arrange
    var userId = 1;
    var explicitTrack = new SpotifyTrack
    {
      Id = "track-1",
      Name = "Song 1",
      Explicit = true,
      Artists = new[] { new SpotifyArtist { Name = "Artist 1" } },
      Album = new SpotifyAlbum { Id = "album-1", Name = "Album 1" },
      Uri = "spotify:track:track-1"
    };

    var searchResponse = new SpotifySearchResponse
    {
      Tracks = new SpotifyTracks
      {
        Items = new SpotifyTrack[0], // No results
        Total = 0
      }
    };

    SetupHttpResponse(HttpStatusCode.OK, JsonSerializer.Serialize(searchResponse));

    // Act
    var result = await _spotifyService.FindCleanVersionAsync(userId, explicitTrack);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task SendWithRetryAsync_WhenHttpRequestFails_ShouldRetryWithExponentialBackoff()
  {
    // Arrange
    var userId = 1;

    // Setup to throw HttpRequestException twice, then succeed
    _httpMessageHandlerMock
        .Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ThrowsAsync(new HttpRequestException("Network error"))
        .ThrowsAsync(new HttpRequestException("Network error"))
        .ReturnsAsync(new HttpResponseMessage
        {
          StatusCode = HttpStatusCode.OK,
          Content = new StringContent(JsonSerializer.Serialize(new SpotifyUserProfile { Id = "test" }))
        });

    // Act & Assert - Should eventually succeed after retries
    var result = await _spotifyService.GetUserProfileAsync(userId);
    Assert.NotNull(result);

    // Verify 3 attempts were made (2 failures + 1 success)
    _httpMessageHandlerMock
        .Protected()
        .Verify(
            "SendAsync",
            Times.Exactly(3),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task SendWithRetryAsync_WhenAllRetriesFail_ShouldThrowException()
  {
    // Arrange
    var userId = 1;

    // Setup to always throw HttpRequestException
    _httpMessageHandlerMock
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ThrowsAsync(new HttpRequestException("Network error"));

    // Act & Assert
    await Assert.ThrowsAsync<HttpRequestException>(() => _spotifyService.GetUserProfileAsync(userId));

    // Verify all 3 attempts were made
    _httpMessageHandlerMock
        .Protected()
        .Verify(
            "SendAsync",
            Times.Exactly(3),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
  }

  public void Dispose()
  {
    _httpClient.Dispose();
  }
}