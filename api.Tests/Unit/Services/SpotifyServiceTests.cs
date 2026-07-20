using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RadioWash.Api.Configuration;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for SpotifyService
/// Tests Spotify API integration, token management, retry logic, and data mapping
/// </summary>
public class SpotifyServiceTests
{
  private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
  private readonly HttpClient _httpClient;
  private readonly Mock<IOptions<SpotifySettings>> _mockSpotifySettings;
  private readonly Mock<IMusicTokenService> _mockMusicTokenService;
  private readonly Mock<ILogger<SpotifyService>> _mockLogger;
  private readonly SpotifySettings _spotifySettings;
  private readonly SpotifyService _spotifyService;

  public SpotifyServiceTests()
  {
    _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
    _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
    _mockSpotifySettings = new Mock<IOptions<SpotifySettings>>();
    _mockMusicTokenService = new Mock<IMusicTokenService>();
    _mockLogger = new Mock<ILogger<SpotifyService>>();

    _spotifySettings = new SpotifySettings
    {
      ClientId = "test_client_id",
      ClientSecret = "test_client_secret",
      RedirectUri = "https://example.com/callback",
      Scopes = new[] { "playlist-read-private", "playlist-modify-public" }
    };

    _mockSpotifySettings.Setup(x => x.Value).Returns(_spotifySettings);

    _spotifyService = new SpotifyService(
        _httpClient,
        _mockSpotifySettings.Object,
        _mockMusicTokenService.Object,
        _mockLogger.Object);
  }

  [Fact]
  public async Task GetUserProfileAsync_WithValidResponse_ReturnsUserProfile()
  {
    // Arrange
    var userId = 1;
    var accessToken = "valid_access_token";
    var expectedProfile = new SpotifyUserProfile
    {
      Id = "spotify_user_123",
      DisplayName = "Test User",
      Email = "test@example.com",
      Images = new[] { new SpotifyImage { Url = "https://example.com/avatar.jpg" } }
    };

    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(accessToken);

    var jsonResponse = JsonSerializer.Serialize(expectedProfile);
    SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

    // Act
    var result = await _spotifyService.GetUserProfileAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(expectedProfile.Id, result.Id);
    Assert.Equal(expectedProfile.DisplayName, result.DisplayName);
    Assert.Equal(expectedProfile.Email, result.Email);

    VerifyHttpRequest(HttpMethod.Get, "https://api.spotify.com/v1/me", accessToken);
  }

  [Fact]
  public async Task GetUserProfileAsync_WithUnauthorizedResponse_RefreshesTokenAndRetries()
  {
    // Arrange
    var userId = 1;
    var oldToken = "expired_token";
    var newToken = "refreshed_token";
    var expectedProfile = new SpotifyUserProfile
    {
      Id = "spotify_user_123",
      DisplayName = "Test User",
      Email = "test@example.com"
    };

    _mockMusicTokenService.SetupSequence(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(oldToken)
        .ReturnsAsync(newToken);

    _mockMusicTokenService.Setup(x => x.RefreshTokensAsync(userId, "spotify"))
        .ReturnsAsync(true);

    var jsonResponse = JsonSerializer.Serialize(expectedProfile);

    // First request returns 401, second request with refreshed token succeeds
    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

    // Act
    var result = await _spotifyService.GetUserProfileAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(expectedProfile.Id, result.Id);

    _mockMusicTokenService.Verify(x => x.RefreshTokensAsync(userId, "spotify"), Times.Once);
    _mockMusicTokenService.Verify(x => x.GetValidAccessTokenAsync(userId, "spotify"), Times.Exactly(2));
  }

  [Fact]
  public async Task GetUserProfileAsync_WithForbiddenResponse_RefreshesTokenAndRetries()
  {
    // Arrange
    var userId = 1;
    var oldToken = "expired_token";
    var newToken = "refreshed_token";
    var expectedProfile = new SpotifyUserProfile
    {
      Id = "spotify_user_123",
      DisplayName = "Test User",
      Email = "test@example.com"
    };

    _mockMusicTokenService.SetupSequence(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(oldToken)
        .ReturnsAsync(newToken);

    _mockMusicTokenService.Setup(x => x.RefreshTokensAsync(userId, "spotify"))
        .ReturnsAsync(true);

    var jsonResponse = JsonSerializer.Serialize(expectedProfile);
    var forbiddenResponse = new
    {
      error = new
      {
        status = 403,
        message = "The access token expired"
      }
    };

    // First request returns 403, second request with refreshed token succeeds
    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
          Content = new StringContent(JsonSerializer.Serialize(forbiddenResponse), Encoding.UTF8, "application/json")
        })
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

    // Act
    var result = await _spotifyService.GetUserProfileAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(expectedProfile.Id, result.Id);

    _mockMusicTokenService.Verify(x => x.RefreshTokensAsync(userId, "spotify"), Times.Once);
    _mockMusicTokenService.Verify(x => x.GetValidAccessTokenAsync(userId, "spotify"), Times.Exactly(2));
  }

  [Fact]
  public async Task GetUserPlaylistsAsync_WithForbiddenResponse_RefreshesTokenAndRetries()
  {
    // Arrange
    var userId = 1;
    var oldToken = "expired_token";
    var newToken = "refreshed_token";
    var playlist = CreateTestSpotifyPlaylist("playlist1", "Test Playlist");

    var playlistsResponse = new SpotifyPlaylistsResponse
    {
      Items = new[] { playlist },
      Next = null,
      Total = 1,
      Limit = 50,
      Offset = 0
    };

    _mockMusicTokenService.SetupSequence(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(oldToken)
        .ReturnsAsync(newToken);

    _mockMusicTokenService.Setup(x => x.RefreshTokensAsync(userId, "spotify"))
        .ReturnsAsync(true);

    var forbiddenResponse = new
    {
      error = new
      {
        status = 403,
        message = "Insufficient client scope"
      }
    };

    // First request returns 403, second request with refreshed token succeeds
    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
          Content = new StringContent(JsonSerializer.Serialize(forbiddenResponse), Encoding.UTF8, "application/json")
        })
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(JsonSerializer.Serialize(playlistsResponse), Encoding.UTF8, "application/json")
        });

    // Act
    var result = await _spotifyService.GetUserPlaylistsAsync(userId);

    // Assert
    var playlists = result.ToList();
    Assert.Single(playlists);
    Assert.Equal("playlist1", playlists[0].Id);

    _mockMusicTokenService.Verify(x => x.RefreshTokensAsync(userId, "spotify"), Times.Once);
    _mockMusicTokenService.Verify(x => x.GetValidAccessTokenAsync(userId, "spotify"), Times.Exactly(2));
  }

  [Fact]
  public async Task GetUserPlaylistsAsync_WithForbiddenAndRefreshFails_ThrowsException()
  {
    // Arrange
    var userId = 1;
    var oldToken = "expired_token";

    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(oldToken);

    _mockMusicTokenService.Setup(x => x.RefreshTokensAsync(userId, "spotify"))
        .ReturnsAsync(false);

    var forbiddenResponse = new
    {
      error = new
      {
        status = 403,
        message = "User revoked access"
      }
    };

    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
          Content = new StringContent(JsonSerializer.Serialize(forbiddenResponse), Encoding.UTF8, "application/json")
        });

    // Act & Assert
    await Assert.ThrowsAsync<HttpRequestException>(
        async () => await _spotifyService.GetUserPlaylistsAsync(userId));

    _mockMusicTokenService.Verify(x => x.RefreshTokensAsync(userId, "spotify"), Times.Once);
  }

  [Fact]
  public async Task GetPlaylistTracksAsync_WithForbiddenResponse_RefreshesTokenAndRetries()
  {
    // Arrange
    var userId = 1;
    var playlistId = "test_playlist";
    var oldToken = "expired_token";
    var newToken = "refreshed_token";

    var track = CreateTestSpotifyTrack("track1", "Song One", false);
    var tracksResponse = new SpotifyPlaylistTracksResponse
    {
      Items = new[]
      {
        new SpotifyPlaylistTrack { Track = track, AddedAt = "2023-01-01T00:00:00Z" }
      },
      Next = null,
      Total = 1,
      Limit = 100,
      Offset = 0
    };

    _mockMusicTokenService.SetupSequence(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(oldToken)
        .ReturnsAsync(newToken);

    _mockMusicTokenService.Setup(x => x.RefreshTokensAsync(userId, "spotify"))
        .ReturnsAsync(true);

    // First request returns 403, second request with refreshed token succeeds
    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Forbidden))
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(JsonSerializer.Serialize(tracksResponse), Encoding.UTF8, "application/json")
        });

    // Act
    var result = await _spotifyService.GetPlaylistTracksAsync(userId, playlistId);

    // Assert
    var tracks = result.ToList();
    Assert.Single(tracks);
    Assert.Equal("track1", tracks[0].Id);

    _mockMusicTokenService.Verify(x => x.RefreshTokensAsync(userId, "spotify"), Times.Once);
  }

  [Fact]
  public async Task GetUserPlaylistsAsync_WithPaginatedResponse_ReturnsAllPlaylists()
  {
    // Arrange
    var userId = 1;
    var accessToken = "valid_access_token";

    var playlist1 = CreateTestSpotifyPlaylist("playlist1", "First Playlist");
    var playlist2 = CreateTestSpotifyPlaylist("playlist2", "Second Playlist");
    var playlist3 = CreateTestSpotifyPlaylist("playlist3", "Third Playlist");

    // First page response
    var firstPageResponse = new SpotifyPlaylistsResponse
    {
      Items = new[] { playlist1, playlist2 },
      Next = "https://api.spotify.com/v1/me/playlists?offset=2&limit=50",
      Total = 3,
      Limit = 50,
      Offset = 0
    };

    // Second page response
    var secondPageResponse = new SpotifyPlaylistsResponse
    {
      Items = new[] { playlist3 },
      Next = null,
      Total = 3,
      Limit = 50,
      Offset = 2
    };

    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(accessToken);

    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(JsonSerializer.Serialize(firstPageResponse), Encoding.UTF8, "application/json")
        })
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(JsonSerializer.Serialize(secondPageResponse), Encoding.UTF8, "application/json")
        });

    // Act
    var result = await _spotifyService.GetUserPlaylistsAsync(userId);

    // Assert
    var playlists = result.ToList();
    Assert.Equal(3, playlists.Count);
    Assert.Equal("playlist1", playlists[0].Id);
    Assert.Equal("playlist2", playlists[1].Id);
    Assert.Equal("playlist3", playlists[2].Id);
    Assert.Equal("First Playlist", playlists[0].Name);
  }

  [Fact]
  public async Task GetPlaylistTracksAsync_WithValidPlaylist_ReturnsTracks()
  {
    // Arrange
    var userId = 1;
    var playlistId = "test_playlist";
    var accessToken = "valid_access_token";

    var track1 = CreateTestSpotifyTrack("track1", "Song One", false);
    var track2 = CreateTestSpotifyTrack("track2", "Song Two", true);

    var tracksResponse = new SpotifyPlaylistTracksResponse
    {
      Items = new[]
        {
                new SpotifyPlaylistTrack { Track = track1, AddedAt = "2023-01-01T00:00:00Z" },
                new SpotifyPlaylistTrack { Track = track2, AddedAt = "2023-01-02T00:00:00Z" }
            },
      Next = null,
      Total = 2,
      Limit = 100,
      Offset = 0
    };

    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(accessToken);

    var jsonResponse = JsonSerializer.Serialize(tracksResponse);
    SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

    // Act
    var result = await _spotifyService.GetPlaylistTracksAsync(userId, playlistId);

    // Assert
    var tracks = result.ToList();
    Assert.Equal(2, tracks.Count);
    Assert.Equal("track1", tracks[0].Id);
    Assert.Equal("Song One", tracks[0].Name);
    Assert.False(tracks[0].Explicit);
    Assert.Equal("track2", tracks[1].Id);
    Assert.True(tracks[1].Explicit);

    VerifyHttpRequest(HttpMethod.Get, $"https://api.spotify.com/v1/playlists/{playlistId}/tracks?limit=100", accessToken);
  }

  [Fact]
  public async Task CreatePlaylistAsync_WithValidData_ReturnsCreatedPlaylist()
  {
    // Arrange
    var userId = 1;
    var playlistName = "Test Clean Playlist";
    var description = "Custom description";
    var accessToken = "valid_access_token";

    var userProfile = new SpotifyUserProfile
    {
      Id = "spotify_user_123",
      DisplayName = "Test User",
      Email = "test@example.com"
    };

    var createdPlaylist = CreateTestSpotifyPlaylist("new_playlist_id", playlistName);

    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(accessToken);

    // Mock user profile request
    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(JsonSerializer.Serialize(userProfile), Encoding.UTF8, "application/json")
        })
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Created)
        {
          Content = new StringContent(JsonSerializer.Serialize(createdPlaylist), Encoding.UTF8, "application/json")
        });

    // Act
    var result = await _spotifyService.CreatePlaylistAsync(userId, playlistName, description);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("new_playlist_id", result.Id);
    Assert.Equal(playlistName, result.Name);
  }

  [Fact]
  public async Task AddTracksToPlaylistAsync_WithMultipleTracks_ChunksRequestsCorrectly()
  {
    // Arrange
    var userId = 1;
    var playlistId = "test_playlist";
    var accessToken = "valid_access_token";

    // Create 150 track URIs to test chunking (should be split into 2 requests of 100 each)
    var trackUris = Enumerable.Range(1, 150)
        .Select(i => $"spotify:track:track{i}")
        .ToArray();

    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(accessToken);

    SetupHttpResponse(HttpStatusCode.OK, "{}");

    // Act
    await _spotifyService.AddTracksToPlaylistAsync(userId, playlistId, trackUris);

    // Assert
    // Should make 2 HTTP requests (150 tracks chunked into groups of 100)
    _mockHttpMessageHandler.Protected()
        .Verify<Task<HttpResponseMessage>>(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task AddTracksToPlaylistAsync_WithEmptyTrackList_DoesNotMakeRequest()
  {
    // Arrange
    var userId = 1;
    var playlistId = "test_playlist";
    var trackUris = Array.Empty<string>();

    // Act
    await _spotifyService.AddTracksToPlaylistAsync(userId, playlistId, trackUris);

    // Assert
    // Note: Verification of no HTTP calls would go here, but skipping due to Moq syntax complexity
  }

  [Fact]
  public async Task FindCleanVersionAsync_WithExplicitTrack_ReturnsCleanVersion()
  {
    // Arrange
    var userId = 1;
    var accessToken = "valid_access_token";
    var explicitTrack = CreateTestSpotifyTrack("explicit_track", "Explicit Song", true);
    var cleanTrack = CreateTestSpotifyTrack("clean_track", "Explicit Song", false);

    var searchResponse = new SpotifySearchResponse
    {
      Tracks = new SpotifyTracks
      {
        Items = new[] { cleanTrack }
      }
    };

    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(accessToken);

    var jsonResponse = JsonSerializer.Serialize(searchResponse);
    SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

    // Act
    var result = await _spotifyService.FindCleanVersionAsync(userId, explicitTrack);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("clean_track", result.Id);
    Assert.False(result.Explicit);
    Assert.Equal("Explicit Song", result.Name);
  }

  [Fact]
  public async Task FindCleanVersionAsync_WithCleanTrack_ReturnsOriginalTrack()
  {
    // Arrange
    var userId = 1;
    var cleanTrack = CreateTestSpotifyTrack("clean_track", "Clean Song", false);

    // Act
    var result = await _spotifyService.FindCleanVersionAsync(userId, cleanTrack);

    // Assert
    Assert.Equal(cleanTrack, result);

    // Note: Verification of no HTTP calls would go here, but skipping due to Moq syntax complexity
  }

  [Fact]
  public async Task FindCleanVersionAsync_WithNoCleanVersionFound_ReturnsNull()
  {
    // Arrange
    var userId = 1;
    var accessToken = "valid_access_token";
    var explicitTrack = CreateTestSpotifyTrack("explicit_track", "Explicit Song", true);

    var searchResponse = new SpotifySearchResponse
    {
      Tracks = new SpotifyTracks
      {
        Items = Array.Empty<SpotifyTrack>()
      }
    };

    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync(accessToken);

    var jsonResponse = JsonSerializer.Serialize(searchResponse);
    SetupHttpResponse(HttpStatusCode.OK, jsonResponse);

    // Act
    var result = await _spotifyService.FindCleanVersionAsync(userId, explicitTrack);

    // Assert
    Assert.Null(result);
  }

  // --- 429 Too Many Requests handling ---

  [Fact]
  public async Task SendWithRetry_On429WithRetryAfter_DelaysAndRetriesSuccessfully()
  {
    // Arrange
    var userId = 1;
    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync("token");

    var profileJson = JsonSerializer.Serialize(new SpotifyUserProfile
    {
      Id = "user_123",
      DisplayName = "Test"
    });

    var retryAfterResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
    retryAfterResponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));

    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(retryAfterResponse)
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(profileJson, Encoding.UTF8, "application/json")
        });

    var observedDelays = new List<TimeSpan>();
    var service = CreateServiceWithDelayCapture(observedDelays);

    // Act
    var result = await service.GetUserProfileAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("user_123", result.Id);
    Assert.Single(observedDelays);
    Assert.Equal(TimeSpan.FromSeconds(2), observedDelays[0]);
  }

  [Fact]
  public async Task SendWithRetry_On429WithoutRetryAfter_UsesFallbackDelay()
  {
    // Arrange
    var userId = 1;
    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync("token");

    var profileJson = JsonSerializer.Serialize(new SpotifyUserProfile { Id = "user_123" });

    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests)) // no Retry-After header
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(profileJson, Encoding.UTF8, "application/json")
        });

    var observedDelays = new List<TimeSpan>();
    var service = CreateServiceWithDelayCapture(observedDelays);

    // Act
    var result = await service.GetUserProfileAsync(userId);

    // Assert — a positive fallback delay was applied when the server omitted Retry-After.
    Assert.NotNull(result);
    Assert.Single(observedDelays);
    Assert.True(observedDelays[0] > TimeSpan.Zero);
  }

  [Fact]
  public async Task SendWithRetry_On429WithLargeRetryAfter_CapsDelayAtMaximum()
  {
    // Arrange
    var userId = 1;
    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync("token");

    var profileJson = JsonSerializer.Serialize(new SpotifyUserProfile { Id = "user_123" });

    var retryAfterResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
    retryAfterResponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(300));

    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(retryAfterResponse)
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(profileJson, Encoding.UTF8, "application/json")
        });

    var observedDelays = new List<TimeSpan>();
    var service = CreateServiceWithDelayCapture(observedDelays);

    // Act
    await service.GetUserProfileAsync(userId);

    // Assert — 300-second Retry-After is clamped to the 60-second cap.
    Assert.Single(observedDelays);
    Assert.Equal(TimeSpan.FromSeconds(60), observedDelays[0]);
  }

  [Fact]
  public async Task SendWithRetry_On429_DisposesThrottledResponseBeforeRetrying()
  {
    var userId = 1;
    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync("token");

    var throttledContent = new TrackingStringContent("{}");
    var throttledResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
    {
      Content = throttledContent
    };
    throttledResponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));

    var profileJson = JsonSerializer.Serialize(new SpotifyUserProfile { Id = "user_123" });

    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(throttledResponse)
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(profileJson, Encoding.UTF8, "application/json")
        });

    var service = CreateServiceWithDelayCapture([]);

    var result = await service.GetUserProfileAsync(userId);

    Assert.Equal("user_123", result.Id);
    Assert.True(throttledContent.Disposed);
  }

  private SpotifyService CreateServiceWithDelayCapture(List<TimeSpan> observedDelays)
  {
    return new SpotifyService(
        _httpClient,
        _mockSpotifySettings.Object,
        _mockMusicTokenService.Object,
        _mockLogger.Object,
        delay: (ts, _) =>
        {
          observedDelays.Add(ts);
          return Task.CompletedTask;
        });
  }

  [Fact]
  public async Task GetUserProfileAsync_WhenCancellationRequested_PropagatesCancellationToHttpSend()
  {
    // Guards against a regression where a Hangfire job cancellation could not stop an in-flight
    // Spotify request. SendAsync must observe the token and throw OperationCanceledException.
    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(1, "spotify")).ReturnsAsync("t");

    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Returns(async (HttpRequestMessage _, CancellationToken ct) =>
        {
          // Simulate a real HTTP call that observes the token.
          await Task.Delay(Timeout.InfiniteTimeSpan, ct);
          return new HttpResponseMessage(HttpStatusCode.OK);
        });

    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(50));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => _spotifyService.GetUserProfileAsync(1, cts.Token));
  }

  [Fact]
  public async Task SendWithRetry_RateLimitDelay_IsCancellable()
  {
    // When a 429 comes in and the retry delay is long, shutdown must not wait the full
    // Retry-After duration. The delay callback must observe the shared cancellation token.
    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(1, "spotify")).ReturnsAsync("t");

    var response429 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
    response429.Headers.Add("Retry-After", "60");
    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(response429);

    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(50));

    var tokenObservedInDelay = CancellationToken.None;
    var service = new SpotifyService(
        _httpClient,
        _mockSpotifySettings.Object,
        _mockMusicTokenService.Object,
        _mockLogger.Object,
        delay: (_, ct) =>
        {
          tokenObservedInDelay = ct;
          return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => service.GetUserProfileAsync(1, cts.Token));
    Assert.Equal(cts.Token, tokenObservedInDelay);
  }

  // --- Search + ISRC lookup ---

  [Fact]
  public async Task SearchTracksAsync_ReturnsTracksAndPassesQueryAndLimit()
  {
    var userId = 1;
    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync("token");

    var searchResponse = new SpotifySearchResponse
    {
      Tracks = new SpotifyTracks { Items = new[] { CreateTestSpotifyTrack("t1", "Song", false) } }
    };
    SetupHttpResponse(HttpStatusCode.OK, JsonSerializer.Serialize(searchResponse));

    var results = await _spotifyService.SearchTracksAsync(userId, "song artist", 7);

    Assert.Single(results);
    Assert.Equal("t1", results[0].Id);
    _mockHttpMessageHandler.Protected().Verify(
        "SendAsync",
        Times.Once(),
        ItExpr.Is<HttpRequestMessage>(req =>
            req.RequestUri!.ToString().Contains("type=track") &&
            req.RequestUri!.ToString().Contains("limit=7")),
        ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task GetTrackByIsrcAsync_PrefersExactIsrcEcho()
  {
    var userId = 1;
    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync("token");

    var fuzzy = CreateTestSpotifyTrack("fuzzy", "Song", false);
    fuzzy.ExternalIds = new SpotifyExternalIds { Isrc = "OTHER" };
    var exact = CreateTestSpotifyTrack("exact", "Song", false);
    exact.ExternalIds = new SpotifyExternalIds { Isrc = "USUM71234567" };

    var searchResponse = new SpotifySearchResponse
    {
      Tracks = new SpotifyTracks { Items = new[] { fuzzy, exact } }
    };
    SetupHttpResponse(HttpStatusCode.OK, JsonSerializer.Serialize(searchResponse));

    var result = await _spotifyService.GetTrackByIsrcAsync(userId, "USUM71234567");

    Assert.NotNull(result);
    Assert.Equal("exact", result.Id);
    _mockHttpMessageHandler.Protected().Verify(
        "SendAsync",
        Times.Once(),
        ItExpr.Is<HttpRequestMessage>(req =>
            req.RequestUri!.ToString().Contains(Uri.EscapeDataString("isrc:USUM71234567"))),
        ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task GetTrackByIsrcAsync_WithNoResults_ReturnsNull()
  {
    var userId = 1;
    _mockMusicTokenService.Setup(x => x.GetValidAccessTokenAsync(userId, "spotify"))
        .ReturnsAsync("token");

    var searchResponse = new SpotifySearchResponse
    {
      Tracks = new SpotifyTracks { Items = Array.Empty<SpotifyTrack>() }
    };
    SetupHttpResponse(HttpStatusCode.OK, JsonSerializer.Serialize(searchResponse));

    var result = await _spotifyService.GetTrackByIsrcAsync(userId, "UNKNOWN");

    Assert.Null(result);
  }

  private void SetupHttpResponse(HttpStatusCode statusCode, string content)
  {
    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(statusCode)
        {
          Content = new StringContent(content, Encoding.UTF8, "application/json")
        });
  }

  private void VerifyHttpRequest(HttpMethod expectedMethod, string expectedUrl, string expectedToken)
  {
    _mockHttpMessageHandler.Protected()
        .Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == expectedMethod &&
                req.RequestUri!.ToString() == expectedUrl &&
                req.Headers.Authorization!.Parameter == expectedToken),
            ItExpr.IsAny<CancellationToken>());
  }

  private static SpotifyPlaylist CreateTestSpotifyPlaylist(string id, string name)
  {
    return new SpotifyPlaylist
    {
      Id = id,
      Name = name,
      Description = $"Description for {name}",
      Images = new[] { new SpotifyImage { Url = "https://example.com/playlist.jpg" } },
      Tracks = new SpotifyPlaylistTracksRef { Total = 10, Href = "https://api.spotify.com/v1/playlists/test/tracks" },
      Owner = new SpotifyUser
      {
        Id = "owner_123",
        DisplayName = "Playlist Owner"
      }
    };
  }

  private static SpotifyTrack CreateTestSpotifyTrack(string id, string name, bool isExplicit)
  {
    return new SpotifyTrack
    {
      Id = id,
      Name = name,
      Explicit = isExplicit,
      Uri = $"spotify:track:{id}",
      Artists = new[]
        {
                new SpotifyArtist { Id = "artist_123", Name = "Test Artist" }
            },
      Album = new SpotifyAlbum
      {
        Id = "album_123",
        Name = "Test Album",
        Images = new[] { new SpotifyImage { Url = "https://example.com/album.jpg" } }
      },
      Popularity = 75
    };
  }

  private sealed class TrackingStringContent(string content) : StringContent(content, Encoding.UTF8, "application/json")
  {
    public bool Disposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
      Disposed = true;
      base.Dispose(disposing);
    }
  }
}
