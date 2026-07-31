using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RadioWash.Api.Configuration;
using RadioWash.Api.Models.AppleMusic;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class AppleMusicService : IAppleMusicService
{
  // Mirrors SpotifyService: cap Retry-After so a hostile/misconfigured header can never
  // block a worker thread for an hour, and fall back to a short delay when it's absent.
  private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
  private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan StorefrontCacheTtl = TimeSpan.FromHours(24);

  // Apple caps search at 25 results per page; write endpoints accept small batches.
  private const int SearchLimitMax = 25;
  private const int IsrcChunkSize = 25;
  private const int CatalogIdsChunkSize = 100;
  private const int AddTracksChunkSize = 25;

  private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

  private readonly HttpClient _httpClient;
  private readonly AppleMusicSettings _settings;
  private readonly IMusicTokenService _musicTokenService;
  private readonly IAppleDeveloperTokenProvider _developerTokenProvider;
  private readonly IMemoryCache _memoryCache;
  private readonly ILogger<AppleMusicService> _logger;
  private readonly Func<TimeSpan, CancellationToken, Task> _delay;

  public AppleMusicService(
      HttpClient httpClient,
      IOptions<AppleMusicSettings> settings,
      IMusicTokenService musicTokenService,
      IAppleDeveloperTokenProvider developerTokenProvider,
      IMemoryCache memoryCache,
      ILogger<AppleMusicService> logger,
      Func<TimeSpan, CancellationToken, Task>? delay = null)
  {
    _httpClient = httpClient;
    _settings = settings.Value;
    _musicTokenService = musicTokenService;
    _developerTokenProvider = developerTokenProvider;
    _memoryCache = memoryCache;
    _logger = logger;
    _delay = delay ?? Task.Delay;
  }

  private async Task<HttpRequestMessage> CreateAppleRequestAsync(HttpMethod method, string url, int userId, CancellationToken cancellationToken)
  {
    var developerToken = await _developerTokenProvider.GetDeveloperTokenAsync(cancellationToken: cancellationToken);
    var userToken = await _musicTokenService.GetValidAccessTokenAsync(userId, MusicProviders.AppleMusic);
    var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", developerToken);
    request.Headers.TryAddWithoutValidation("Music-User-Token", userToken);
    return request;
  }

  private async Task<HttpResponseMessage> SendWithRetryAsync(
    HttpRequestMessage request,
    int userId,
    CancellationToken cancellationToken,
    int maxRetries = 3)
  {
    // Tracked separately from `attempt` so a preceding 429 can't consume the one developer-
    // token regeneration: the two failures have unrelated causes and unrelated remedies.
    var developerTokenRegenerated = false;

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
      try
      {
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
        {
          var retryAfter = response.Headers.RetryAfter?.Delta ?? DefaultRetryAfter;
          if (retryAfter > MaxRetryAfter)
          {
            _logger.LogWarning(
              "Apple Music 429 Retry-After {Requested}s exceeds cap; clamping to {Cap}s",
              retryAfter.TotalSeconds, MaxRetryAfter.TotalSeconds);
            retryAfter = MaxRetryAfter;
          }
          _logger.LogWarning(
            "Apple Music rate-limited (429); waiting {Delay}s before retry {Attempt}/{MaxRetries}",
            retryAfter.TotalSeconds, attempt + 1, maxRetries);

          response.Dispose();
          await _delay(retryAfter, cancellationToken);

          request = CloneRequestForRetry(request);
          continue;
        }

        // 401 means Apple rejected the developer token (the app credential, not the user's).
        // Regenerate it once and retry; a second 401 is a configuration problem.
        if (response.StatusCode == HttpStatusCode.Unauthorized && !developerTokenRegenerated)
        {
          _logger.LogWarning("Apple Music API returned 401; regenerating developer token and retrying");
          response.Dispose();
          developerTokenRegenerated = true;

          var freshToken = await _developerTokenProvider.GetDeveloperTokenAsync(forceRefresh: true, cancellationToken);
          request = CloneRequestForRetry(request);
          request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
          continue;
        }

        // 403 means the Music User Token is invalid or revoked. There is no refresh flow
        // for MUTs — the user must re-authorize through MusicKit.
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
          var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
          _logger.LogWarning("Apple Music 403 for user {UserId}: {ErrorBody}", userId, errorBody);
          response.Dispose();
          throw new UnauthorizedAccessException(
            "Apple Music authorization expired or was revoked; the user must reconnect Apple Music.");
        }

        return response;
      }
      catch (HttpRequestException ex) when (attempt < maxRetries)
      {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        _logger.LogWarning(ex, "HTTP request failed (attempt {Attempt}/{MaxRetries}), retrying after {Delay}s",
          attempt, maxRetries, delay.TotalSeconds);
        await _delay(delay, cancellationToken);

        request = CloneRequestForRetry(request);
      }
    }

    throw new HttpRequestException($"Failed to complete Apple Music API request after {maxRetries} attempts");
  }

  private static HttpRequestMessage CloneRequestForRetry(HttpRequestMessage original)
  {
    var clone = new HttpRequestMessage(original.Method, original.RequestUri)
    {
      Content = original.Content
    };
    foreach (var header in original.Headers.Where(h => h.Key != "Authorization"))
    {
      clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
    }
    if (original.Headers.Authorization != null)
    {
      clone.Headers.Authorization = original.Headers.Authorization;
    }
    return clone;
  }

  public async Task<string> GetUserStorefrontAsync(int userId, CancellationToken cancellationToken = default)
  {
    var cacheKey = $"apple_music:storefront:{userId}";
    if (_memoryCache.TryGetValue(cacheKey, out string? cached) && cached != null)
    {
      return cached;
    }

    try
    {
      var request = await CreateAppleRequestAsync(HttpMethod.Get, $"{_settings.ApiBaseUrl}/me/storefront", userId, cancellationToken);
      var response = await SendWithRetryAsync(request, userId, cancellationToken);
      response.EnsureSuccessStatusCode();

      var json = await response.Content.ReadAsStringAsync(cancellationToken);
      var storefrontResponse = JsonSerializer.Deserialize<AppleStorefrontResponse>(json, JsonOptions);
      var storefront = storefrontResponse?.Data.FirstOrDefault()?.Id;

      if (string.IsNullOrEmpty(storefront))
      {
        _logger.LogWarning("Apple Music storefront response empty for user {UserId}; using default '{Default}'",
          userId, _settings.DefaultStorefront);
        return _settings.DefaultStorefront;
      }

      _memoryCache.Set(cacheKey, storefront, StorefrontCacheTtl);
      return storefront;
    }
    catch (UnauthorizedAccessException)
    {
      throw; // reconnect required — never mask with the default storefront
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException)
    {
      _logger.LogWarning(ex, "Failed to resolve Apple Music storefront for user {UserId}; using default '{Default}'",
        userId, _settings.DefaultStorefront);
      return _settings.DefaultStorefront;
    }
  }

  public async Task<IEnumerable<AppleLibraryPlaylist>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken = default)
  {
    var playlists = new List<AppleLibraryPlaylist>();
    var url = $"{_settings.ApiBaseUrl}/me/library/playlists?limit=100";

    while (!string.IsNullOrEmpty(url))
    {
      var request = await CreateAppleRequestAsync(HttpMethod.Get, url, userId, cancellationToken);
      var response = await SendWithRetryAsync(request, userId, cancellationToken);
      response.EnsureSuccessStatusCode();

      var json = await response.Content.ReadAsStringAsync(cancellationToken);
      var page = JsonSerializer.Deserialize<ApplePagedResponse<AppleLibraryPlaylist>>(json, JsonOptions)
          ?? throw new Exception("Failed to deserialize Apple Music playlists response.");

      playlists.AddRange(page.Data);
      url = ResolveNextUrl(page.Next);
    }

    return playlists;
  }

  public async Task<IEnumerable<AppleLibrarySong>> GetPlaylistTracksAsync(int userId, string playlistId, CancellationToken cancellationToken = default)
  {
    var tracks = new List<AppleLibrarySong>();
    var url = $"{_settings.ApiBaseUrl}/me/library/playlists/{playlistId}/tracks?include=catalog&limit=100";

    while (!string.IsNullOrEmpty(url))
    {
      var request = await CreateAppleRequestAsync(HttpMethod.Get, url, userId, cancellationToken);
      var response = await SendWithRetryAsync(request, userId, cancellationToken);

      // Apple returns 404 for the tracks collection of an empty playlist.
      if (response.StatusCode == HttpStatusCode.NotFound && tracks.Count == 0)
      {
        return tracks;
      }
      response.EnsureSuccessStatusCode();

      var json = await response.Content.ReadAsStringAsync(cancellationToken);
      var page = JsonSerializer.Deserialize<ApplePagedResponse<AppleLibrarySong>>(json, JsonOptions)
          ?? throw new Exception("Failed to deserialize Apple Music playlist tracks response.");

      tracks.AddRange(page.Data);
      url = ResolveNextUrl(page.Next);
    }

    return tracks;
  }

  public async Task<IReadOnlyList<AppleCatalogSong>> GetCatalogSongsByIsrcAsync(int userId, IReadOnlyCollection<string> isrcs, CancellationToken cancellationToken = default)
  {
    if (isrcs.Count == 0) return Array.Empty<AppleCatalogSong>();

    var storefront = await GetUserStorefrontAsync(userId, cancellationToken);
    var songs = new List<AppleCatalogSong>();

    foreach (var chunk in isrcs.Chunk(IsrcChunkSize))
    {
      var filter = Uri.EscapeDataString(string.Join(",", chunk));
      var url = $"{_settings.ApiBaseUrl}/catalog/{storefront}/songs?filter[isrc]={filter}";
      songs.AddRange(await GetCatalogSongsPageAsync(url, userId, cancellationToken));
    }

    return songs;
  }

  public async Task<IReadOnlyList<AppleCatalogSong>> GetCatalogSongsByIdsAsync(int userId, IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
  {
    if (ids.Count == 0) return Array.Empty<AppleCatalogSong>();

    var storefront = await GetUserStorefrontAsync(userId, cancellationToken);
    var songs = new List<AppleCatalogSong>();

    foreach (var chunk in ids.Chunk(CatalogIdsChunkSize))
    {
      var idList = Uri.EscapeDataString(string.Join(",", chunk));
      var url = $"{_settings.ApiBaseUrl}/catalog/{storefront}/songs?ids={idList}";
      songs.AddRange(await GetCatalogSongsPageAsync(url, userId, cancellationToken));
    }

    return songs;
  }

  private async Task<AppleCatalogSong[]> GetCatalogSongsPageAsync(string url, int userId, CancellationToken cancellationToken)
  {
    var request = await CreateAppleRequestAsync(HttpMethod.Get, url, userId, cancellationToken);
    var response = await SendWithRetryAsync(request, userId, cancellationToken);

    // A lookup where no id/ISRC matches yields 404; treat as "no results".
    if (response.StatusCode == HttpStatusCode.NotFound)
    {
      return Array.Empty<AppleCatalogSong>();
    }
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    var page = JsonSerializer.Deserialize<ApplePagedResponse<AppleCatalogSong>>(json, JsonOptions);
    return page?.Data ?? Array.Empty<AppleCatalogSong>();
  }

  public async Task<IReadOnlyList<AppleCatalogSong>> SearchCatalogSongsAsync(int userId, string term, int limit, CancellationToken cancellationToken = default)
  {
    var storefront = await GetUserStorefrontAsync(userId, cancellationToken);
    var encodedTerm = Uri.EscapeDataString(term);
    var clampedLimit = Math.Clamp(limit, 1, SearchLimitMax);
    var url = $"{_settings.ApiBaseUrl}/catalog/{storefront}/search?types=songs&term={encodedTerm}&limit={clampedLimit}";

    var request = await CreateAppleRequestAsync(HttpMethod.Get, url, userId, cancellationToken);
    var response = await SendWithRetryAsync(request, userId, cancellationToken);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    var searchResponse = JsonSerializer.Deserialize<AppleSearchResponse>(json, JsonOptions);
    return searchResponse?.Results?.Songs?.Data ?? Array.Empty<AppleCatalogSong>();
  }

  public async Task<AppleLibraryPlaylist> CreateLibraryPlaylistAsync(int userId, string name, string? description, CancellationToken cancellationToken = default)
  {
    var url = $"{_settings.ApiBaseUrl}/me/library/playlists";
    var request = await CreateAppleRequestAsync(HttpMethod.Post, url, userId, cancellationToken);

    var payload = new
    {
      attributes = new
      {
        name,
        description = description ?? $"Created by RadioWash"
      }
    };
    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    var response = await SendWithRetryAsync(request, userId, cancellationToken);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    var created = JsonSerializer.Deserialize<ApplePagedResponse<AppleLibraryPlaylist>>(json, JsonOptions);
    return created?.Data.FirstOrDefault()
        ?? throw new Exception("Failed to deserialize created Apple Music playlist.");
  }

  public async Task AddTracksToLibraryPlaylistAsync(int userId, string playlistId, IEnumerable<string> catalogSongIds, CancellationToken cancellationToken = default)
  {
    if (!catalogSongIds.Any()) return;

    foreach (var chunk in catalogSongIds.Chunk(AddTracksChunkSize))
    {
      var url = $"{_settings.ApiBaseUrl}/me/library/playlists/{playlistId}/tracks";
      var request = await CreateAppleRequestAsync(HttpMethod.Post, url, userId, cancellationToken);
      var payload = new { data = chunk.Select(id => new { id, type = "songs" }).ToArray() };
      request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
      var response = await SendWithRetryAsync(request, userId, cancellationToken);
      response.EnsureSuccessStatusCode();
    }
  }

  // Apple returns "next" as a path relative to the API host ("/v1/me/..."); resolve it
  // against the configured base URL's host.
  private string? ResolveNextUrl(string? next)
  {
    if (string.IsNullOrEmpty(next)) return null;
    if (next.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return next;
    return new Uri(new Uri(_settings.ApiBaseUrl), next).ToString();
  }
}
