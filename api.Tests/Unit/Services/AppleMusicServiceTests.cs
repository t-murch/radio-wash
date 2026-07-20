using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RadioWash.Api.Configuration;
using RadioWash.Api.Models.AppleMusic;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for AppleMusicService, mirroring SpotifyServiceTests' HttpMessageHandler
/// pattern. Apple-specific contracts pinned here: every request carries the developer token
/// AND the Music-User-Token; the storefront is cached per user; paging follows Apple's
/// relative "next" paths; a 401 regenerates the developer token once; a 403 surfaces as
/// UnauthorizedAccessException (no MUT refresh flow exists); lookups chunk correctly.
/// </summary>
public class AppleMusicServiceTests
{
  private const int UserId = 1;
  private const string DeveloperToken = "dev_token";
  private const string UserToken = "music_user_token";
  private const string ApiBase = "https://api.music.apple.com/v1";

  private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler = new();
  private readonly HttpClient _httpClient;
  private readonly Mock<IMusicTokenService> _mockMusicTokenService = new();
  private readonly Mock<IAppleDeveloperTokenProvider> _mockDeveloperTokenProvider = new();
  private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
  private readonly Mock<ILogger<AppleMusicService>> _mockLogger = new();
  private readonly AppleMusicService _service;

  public AppleMusicServiceTests()
  {
    _httpClient = new HttpClient(_mockHttpMessageHandler.Object);

    _mockDeveloperTokenProvider
        .Setup(x => x.GetDeveloperTokenAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(DeveloperToken);
    _mockMusicTokenService
        .Setup(x => x.GetValidAccessTokenAsync(UserId, "apple_music"))
        .ReturnsAsync(UserToken);

    _service = CreateService();
  }

  private AppleMusicService CreateService(Func<TimeSpan, CancellationToken, Task>? delay = null)
  {
    return new AppleMusicService(
        _httpClient,
        Options.Create(new AppleMusicSettings { TeamId = "team", KeyId = "key" }),
        _mockMusicTokenService.Object,
        _mockDeveloperTokenProvider.Object,
        _memoryCache,
        _mockLogger.Object,
        delay);
  }

  private void CacheStorefront(string storefront = "us")
  {
    _memoryCache.Set($"apple_music:storefront:{UserId}", storefront);
  }

  private void SetupHttpResponse(HttpStatusCode statusCode, string content)
  {
    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(() => new HttpResponseMessage(statusCode)
        {
          Content = new StringContent(content, Encoding.UTF8, "application/json")
        });
  }

  private static string PlaylistsJson(string? next, params AppleLibraryPlaylist[] playlists) =>
      JsonSerializer.Serialize(new ApplePagedResponse<AppleLibraryPlaylist> { Data = playlists, Next = next });

  private static AppleLibraryPlaylist CreatePlaylist(string id, string name) => new()
  {
    Id = id,
    Attributes = new AppleLibraryPlaylistAttributes { Name = name, CanEdit = true }
  };

  private static AppleCatalogSong CreateCatalogSong(string id, string name, string? contentRating = null, string? isrc = null) => new()
  {
    Id = id,
    Attributes = new AppleCatalogSongAttributes
    {
      Name = name,
      ArtistName = "Test Artist",
      ContentRating = contentRating,
      Isrc = isrc
    }
  };

  [Fact]
  public async Task GetUserPlaylistsAsync_SendsDeveloperTokenAndMusicUserToken()
  {
    SetupHttpResponse(HttpStatusCode.OK, PlaylistsJson(null, CreatePlaylist("p.1", "Mix")));

    var result = await _service.GetUserPlaylistsAsync(UserId);

    Assert.Single(result);
    _mockHttpMessageHandler.Protected().Verify(
        "SendAsync",
        Times.Once(),
        ItExpr.Is<HttpRequestMessage>(req =>
            req.Headers.Authorization!.Parameter == DeveloperToken &&
            req.Headers.GetValues("Music-User-Token").Single() == UserToken),
        ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task GetUserPlaylistsAsync_FollowsRelativeNextPaths()
  {
    var firstPage = PlaylistsJson("/v1/me/library/playlists?offset=100", CreatePlaylist("p.1", "One"));
    var secondPage = PlaylistsJson(null, CreatePlaylist("p.2", "Two"));

    var requestedUrls = new List<string>();
    var responses = new Queue<HttpResponseMessage>(new[]
    {
      new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(firstPage, Encoding.UTF8, "application/json")
      },
      new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(secondPage, Encoding.UTF8, "application/json")
      }
    });
    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Callback<HttpRequestMessage, CancellationToken>((req, _) => requestedUrls.Add(req.RequestUri!.ToString()))
        .ReturnsAsync(() => responses.Dequeue());

    var result = await _service.GetUserPlaylistsAsync(UserId);

    Assert.Equal(2, result.Count());
    Assert.Equal($"{ApiBase}/me/library/playlists?limit=100", requestedUrls[0]);
    // The relative next path resolves against the API host.
    Assert.Equal("https://api.music.apple.com/v1/me/library/playlists?offset=100", requestedUrls[1]);
  }

  [Fact]
  public async Task GetUserStorefrontAsync_CachesResultPerUser()
  {
    SetupHttpResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new AppleStorefrontResponse
    {
      Data = new[] { new AppleStorefront { Id = "gb" } }
    }));

    var first = await _service.GetUserStorefrontAsync(UserId);
    var second = await _service.GetUserStorefrontAsync(UserId);

    Assert.Equal("gb", first);
    Assert.Equal("gb", second);
    _mockHttpMessageHandler.Protected().Verify(
        "SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
  }

  [Fact]
  public async Task GetUserStorefrontAsync_OnHttpFailure_FallsBackToDefault()
  {
    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ThrowsAsync(new HttpRequestException("network down"));

    var storefront = await _service.GetUserStorefrontAsync(UserId);

    Assert.Equal("us", storefront);
  }

  [Fact]
  public async Task GetPlaylistTracksAsync_EmptyPlaylistReturns404_YieldsEmptyList()
  {
    // Apple's API 404s on the tracks collection of an empty library playlist.
    SetupHttpResponse(HttpStatusCode.NotFound, "{}");

    var tracks = await _service.GetPlaylistTracksAsync(UserId, "p.empty");

    Assert.Empty(tracks);
  }

  [Fact]
  public async Task SendWithRetry_On429WithRetryAfter_DelaysAndRetries()
  {
    var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
    throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));

    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(throttled)
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(PlaylistsJson(null, CreatePlaylist("p.1", "Mix")), Encoding.UTF8, "application/json")
        });

    var observedDelays = new List<TimeSpan>();
    var service = CreateService((ts, _) =>
    {
      observedDelays.Add(ts);
      return Task.CompletedTask;
    });

    var result = await service.GetUserPlaylistsAsync(UserId);

    Assert.Single(result);
    Assert.Single(observedDelays);
    Assert.Equal(TimeSpan.FromSeconds(2), observedDelays[0]);
  }

  [Fact]
  public async Task SendWithRetry_On429WithLargeRetryAfter_CapsDelayAtMaximum()
  {
    var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
    throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(300));

    _mockHttpMessageHandler.Protected()
        .SetupSequence<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(throttled)
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(PlaylistsJson(null), Encoding.UTF8, "application/json")
        });

    var observedDelays = new List<TimeSpan>();
    var service = CreateService((ts, _) =>
    {
      observedDelays.Add(ts);
      return Task.CompletedTask;
    });

    await service.GetUserPlaylistsAsync(UserId);

    Assert.Single(observedDelays);
    Assert.Equal(TimeSpan.FromSeconds(60), observedDelays[0]);
  }

  [Fact]
  public async Task SendWithRetry_On401_RegeneratesDeveloperTokenOnceAndRetries()
  {
    _mockDeveloperTokenProvider
        .SetupSequence(x => x.GetDeveloperTokenAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync("stale_dev_token")
        .ReturnsAsync("fresh_dev_token");

    var authHeaders = new List<string?>();
    var responses = new Queue<HttpResponseMessage>(new[]
    {
      new HttpResponseMessage(HttpStatusCode.Unauthorized),
      new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(PlaylistsJson(null, CreatePlaylist("p.1", "Mix")), Encoding.UTF8, "application/json")
      }
    });
    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Callback<HttpRequestMessage, CancellationToken>((req, _) => authHeaders.Add(req.Headers.Authorization?.Parameter))
        .ReturnsAsync(() => responses.Dequeue());

    var result = await _service.GetUserPlaylistsAsync(UserId);

    Assert.Single(result);
    Assert.Equal(new[] { "stale_dev_token", "fresh_dev_token" }, authHeaders);
    _mockDeveloperTokenProvider.Verify(
        x => x.GetDeveloperTokenAsync(true, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task SendWithRetry_On403_ThrowsUnauthorizedAccess()
  {
    // 403 = Music User Token invalid/revoked. There is no refresh flow — the user must
    // reconnect via MusicKit, so the error must surface as UnauthorizedAccessException.
    SetupHttpResponse(HttpStatusCode.Forbidden, "{\"errors\":[{\"status\":\"403\"}]}");

    var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
        () => _service.GetUserPlaylistsAsync(UserId));
    Assert.Contains("reconnect", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task GetCatalogSongsByIsrcAsync_ChunksInto25PerRequest()
  {
    CacheStorefront();
    var isrcs = Enumerable.Range(1, 26).Select(i => $"ISRC{i:D2}").ToArray();

    var requestedUrls = new List<string>();
    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Callback<HttpRequestMessage, CancellationToken>((req, _) => requestedUrls.Add(req.RequestUri!.ToString()))
        .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(
            JsonSerializer.Serialize(new ApplePagedResponse<AppleCatalogSong>
            {
              Data = new[] { CreateCatalogSong("s1", "Song") }
            }), Encoding.UTF8, "application/json")
        });

    var songs = await _service.GetCatalogSongsByIsrcAsync(UserId, isrcs);

    Assert.Equal(2, requestedUrls.Count);
    Assert.All(requestedUrls, url => Assert.Contains("/catalog/us/songs?filter[isrc]=", Uri.UnescapeDataString(url)));
    Assert.Equal(2, songs.Count);
  }

  [Fact]
  public async Task GetCatalogSongsByIsrcAsync_WithNoMatches404_ReturnsEmpty()
  {
    CacheStorefront();
    SetupHttpResponse(HttpStatusCode.NotFound, "{}");

    var songs = await _service.GetCatalogSongsByIsrcAsync(UserId, new[] { "UNKNOWN" });

    Assert.Empty(songs);
  }

  [Fact]
  public async Task SearchCatalogSongsAsync_ClampsLimitAndParsesResults()
  {
    CacheStorefront();
    string? requestedUrl = null;
    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Callback<HttpRequestMessage, CancellationToken>((req, _) => requestedUrl = req.RequestUri!.ToString())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(
            JsonSerializer.Serialize(new AppleSearchResponse
            {
              Results = new AppleSearchResults
              {
                Songs = new AppleSearchSongs { Data = new[] { CreateCatalogSong("s1", "Hit", "clean") } }
              }
            }), Encoding.UTF8, "application/json")
        });

    var songs = await _service.SearchCatalogSongsAsync(UserId, "hit song artist", 100);

    Assert.Single(songs);
    Assert.Equal("clean", songs[0].Attributes.ContentRating);
    Assert.Contains("limit=25", requestedUrl);
    Assert.Contains("types=songs", requestedUrl);
  }

  [Fact]
  public async Task CreateLibraryPlaylistAsync_ReturnsCreatedPlaylist()
  {
    SetupHttpResponse(HttpStatusCode.Created, PlaylistsJson(null, CreatePlaylist("p.new", "Clean Mix")));

    var playlist = await _service.CreateLibraryPlaylistAsync(UserId, "Clean Mix", "desc");

    Assert.Equal("p.new", playlist.Id);
    Assert.Equal("Clean Mix", playlist.Attributes.Name);
  }

  [Fact]
  public async Task AddTracksToLibraryPlaylistAsync_ChunksInto25PerRequest()
  {
    var bodies = new List<string>();
    _mockHttpMessageHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Returns(async (HttpRequestMessage req, CancellationToken _) =>
        {
          bodies.Add(await req.Content!.ReadAsStringAsync());
          return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

    var ids = Enumerable.Range(1, 26).Select(i => $"cat{i}").ToArray();
    await _service.AddTracksToLibraryPlaylistAsync(UserId, "p.1", ids);

    Assert.Equal(2, bodies.Count);
    Assert.Contains("\"type\":\"songs\"", bodies[0]);
    Assert.Contains("\"cat1\"", bodies[0]);
    Assert.Contains("\"cat26\"", bodies[1]);
  }

  [Fact]
  public async Task AddTracksToLibraryPlaylistAsync_WithEmptyList_MakesNoRequest()
  {
    await _service.AddTracksToLibraryPlaylistAsync(UserId, "p.1", Array.Empty<string>());

    _mockHttpMessageHandler.Protected().Verify(
        "SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
  }
}
