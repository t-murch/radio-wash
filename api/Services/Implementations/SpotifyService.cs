using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RadioWash.Api.Configuration;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class SpotifyService : ISpotifyService
{
  // Cap Retry-After at 60s. Spotify has sent hour-long Retry-After values in edge cases; we
  // would rather fail fast and let Hangfire reschedule than block a worker thread for an hour.
  private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
  // Fallback delay when a 429 response omits Retry-After.
  private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(5);

  private readonly HttpClient _httpClient;
  private readonly SpotifySettings _spotifySettings;
  private readonly IMusicTokenService _musicTokenService;
  private readonly ILogger<SpotifyService> _logger;
  private readonly Func<TimeSpan, CancellationToken, Task> _delay;

  public SpotifyService(
      HttpClient httpClient,
      IOptions<SpotifySettings> spotifySettings,
      IMusicTokenService musicTokenService,
      ILogger<SpotifyService> logger,
      Func<TimeSpan, CancellationToken, Task>? delay = null)
  {
    _httpClient = httpClient;
    _spotifySettings = spotifySettings.Value;
    _musicTokenService = musicTokenService;
    _logger = logger;
    _delay = delay ?? Task.Delay;
  }

  // Secure token retrieval with automatic refresh and retry logic
  private async Task<HttpRequestMessage> CreateSpotifyRequestAsync(HttpMethod method, string url, int userId)
  {
    var accessToken = await _musicTokenService.GetValidAccessTokenAsync(userId, "spotify");
    var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    return request;
  }

  // Retry wrapper for API calls with exponential backoff
  private async Task<HttpResponseMessage> SendWithRetryAsync(
    HttpRequestMessage request,
    int userId,
    CancellationToken cancellationToken,
    int maxRetries = 3)
  {
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
      try
      {
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // Rate-limit: honor Retry-After when present, otherwise a bounded fallback, and never
        // block for longer than MaxRetryAfter no matter what the server returns.
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxRetries)
        {
          var retryAfter = response.Headers.RetryAfter?.Delta ?? DefaultRetryAfter;
          if (retryAfter > MaxRetryAfter)
          {
            _logger.LogWarning(
              "Spotify 429 Retry-After {Requested}s exceeds cap; clamping to {Cap}s",
              retryAfter.TotalSeconds, MaxRetryAfter.TotalSeconds);
            retryAfter = MaxRetryAfter;
          }
          _logger.LogWarning(
            "Spotify rate-limited (429); waiting {Delay}s before retry {Attempt}/{MaxRetries}",
            retryAfter.TotalSeconds, attempt + 1, maxRetries);

          response.Dispose();
          await _delay(retryAfter, cancellationToken);

          request = CloneRequestForRetry(request);
          continue;
        }

        // If unauthorized or forbidden, try to refresh token and retry once more
        // Spotify sometimes returns 403 instead of 401 for expired/revoked tokens
        if ((response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
             response.StatusCode == System.Net.HttpStatusCode.Forbidden) && attempt == 1)
        {
          _logger.LogWarning("Spotify API returned {StatusCode}, attempting token refresh for user {UserId}",
            (int)response.StatusCode, userId);

          // Log response body for 403 errors to capture Spotify's error details
          if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
          {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Spotify 403 response body: {ErrorBody}", errorBody);
          }

          var refreshed = await _musicTokenService.RefreshTokensAsync(userId, "spotify");
          if (refreshed)
          {
            response.Dispose();
            // Recreate request with new token (HttpRequestMessage can only be sent once)
            var newToken = await _musicTokenService.GetValidAccessTokenAsync(userId, "spotify");
            var originalContent = request.Content;
            var originalHeaders = request.Headers.Where(h => h.Key != "Authorization").ToList();

            request = new HttpRequestMessage(request.Method, request.RequestUri);
            request.Content = originalContent;

            // Copy original headers except Authorization (will be set with new token)
            foreach (var header in originalHeaders)
            {
              request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
            _logger.LogInformation("Token refreshed successfully, retrying request for user {UserId}", userId);
            continue; // Retry with new token
          }
          else
          {
            _logger.LogError("Token refresh failed for user {UserId}. User may need to re-authenticate with Spotify", userId);
          }
        }

        return response;
      }
      catch (HttpRequestException ex) when (attempt < maxRetries)
      {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
        _logger.LogWarning(ex, "HTTP request failed (attempt {Attempt}/{MaxRetries}), retrying after {Delay}s",
          attempt, maxRetries, delay.TotalSeconds);
        await _delay(delay, cancellationToken);

        request = CloneRequestForRetry(request);
      }
    }

    throw new HttpRequestException($"Failed to complete Spotify API request after {maxRetries} attempts");
  }

  // HttpRequestMessage can only be sent once; clone it (method, URI, content, non-auth headers)
  // so the retry loop can re-issue the same request.
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

  public async Task<SpotifyUserProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken = default)
  {
    var request = await CreateSpotifyRequestAsync(HttpMethod.Get, $"{_spotifySettings.ApiBaseUrl}/me", userId);
    var response = await SendWithRetryAsync(request, userId, cancellationToken);
    response.EnsureSuccessStatusCode();
    var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonSerializer.Deserialize<SpotifyUserProfile>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new Exception("Failed to deserialize user profile.");
  }

  public async Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken = default)
  {
    var playlists = new List<SpotifyPlaylist>();
    var url = $"{_spotifySettings.ApiBaseUrl}/me/playlists?limit=50";

    while (!string.IsNullOrEmpty(url))
    {
      var request = await CreateSpotifyRequestAsync(HttpMethod.Get, url, userId);
      var response = await SendWithRetryAsync(request, userId, cancellationToken);
      response.EnsureSuccessStatusCode();

      var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
      var playlistsResponse = JsonSerializer.Deserialize<SpotifyPlaylistsResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

      if (playlistsResponse?.Items == null) throw new Exception("Failed to deserialize playlists response.");

      playlists.AddRange(playlistsResponse.Items);
      url = playlistsResponse.Next; // Continue to the next page if it exists
    }

    return playlists.Select(p => new PlaylistDto
    {
      Id = p.Id,
      Name = p.Name,
      Description = p.Description,
      ImageUrl = p.Images?.FirstOrDefault()?.Url,
      TrackCount = p.Tracks.Total,
      OwnerId = p.Owner.Id,
      OwnerName = p.Owner.DisplayName
    });
  }

  public async Task<IEnumerable<SpotifyTrack>> GetPlaylistTracksAsync(int userId, string playlistId, CancellationToken cancellationToken = default)
  {
    var tracks = new List<SpotifyTrack>();
    var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks?limit=100";

    while (!string.IsNullOrEmpty(url))
    {
      var request = await CreateSpotifyRequestAsync(HttpMethod.Get, url, userId);
      var response = await SendWithRetryAsync(request, userId, cancellationToken);
      response.EnsureSuccessStatusCode();

      var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
      var tracksResponse = JsonSerializer.Deserialize<SpotifyPlaylistTracksResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

      if (tracksResponse?.Items == null) throw new Exception("Failed to deserialize tracks response.");

      // Filter out potential null tracks and tracks with null/empty IDs (local files, unavailable tracks, etc.)
      tracks.AddRange(tracksResponse.Items
        .Where(i => i.Track != null && !string.IsNullOrEmpty(i.Track.Id))
        .Select(i => i.Track!));
      url = tracksResponse.Next;
    }
    return tracks;
  }

  public async Task<SpotifyPlaylist> CreatePlaylistAsync(int userId, string name, string? description = null, CancellationToken cancellationToken = default)
  {
    var userProfile = await GetUserProfileAsync(userId, cancellationToken);
    var url = $"{_spotifySettings.ApiBaseUrl}/users/{userProfile.Id}/playlists";
    var request = await CreateSpotifyRequestAsync(HttpMethod.Post, url, userId);

    var payload = new { name, description = description ?? $"Clean version of {name} created by RadioWash", @public = false };
    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    var response = await SendWithRetryAsync(request, userId, cancellationToken);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonSerializer.Deserialize<SpotifyPlaylist>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new Exception("Failed to deserialize created playlist.");
  }

  public async Task AddTracksToPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackUris, CancellationToken cancellationToken = default)
  {
    if (!trackUris.Any()) return;

    foreach (var uriChunk in trackUris.Chunk(100))
    {
      var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks";
      var request = await CreateSpotifyRequestAsync(HttpMethod.Post, url, userId);
      var payload = new { uris = uriChunk };
      request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
      var response = await SendWithRetryAsync(request, userId, cancellationToken);
      response.EnsureSuccessStatusCode();
    }
  }

  public async Task RemoveTracksFromPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackUris, CancellationToken cancellationToken = default)
  {
    if (!trackUris.Any()) return;

    foreach (var uriChunk in trackUris.Chunk(100))
    {
      var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks";
      var request = await CreateSpotifyRequestAsync(HttpMethod.Delete, url, userId);
      var tracks = uriChunk.Select(uri => new { uri }).ToArray();
      var payload = new { tracks };
      request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
      var response = await SendWithRetryAsync(request, userId, cancellationToken);
      response.EnsureSuccessStatusCode();
    }
  }

  public async Task<SpotifyTrack?> FindCleanVersionAsync(int userId, SpotifyTrack explicitTrack, CancellationToken cancellationToken = default)
  {
    if (!explicitTrack.Explicit) return explicitTrack;

    var artists = string.Join(" ", explicitTrack.Artists.Select(a => a.Name));
    // Construct a search query that excludes "explicit" and looks for the same track name and artist.
    var query = $"{explicitTrack.Name} {artists} -tag:explicit";
    var encodedQuery = Uri.EscapeDataString(query);
    var url = $"{_spotifySettings.ApiBaseUrl}/search?q={encodedQuery}&type=track&limit=5";

    var request = await CreateSpotifyRequestAsync(HttpMethod.Get, url, userId);
    var response = await SendWithRetryAsync(request, userId, cancellationToken);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
    var searchResponse = JsonSerializer.Deserialize<SpotifySearchResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    // Find the best non-explicit match with same artist(s)
    return searchResponse?.Tracks?.Items?.FirstOrDefault(t => 
        !t.Explicit && 
        t.Name.Equals(explicitTrack.Name, StringComparison.OrdinalIgnoreCase) &&
        HasMatchingArtist(explicitTrack.Artists, t.Artists)
    );
  }

  /// <summary>
  /// Checks if two artist arrays have at least one matching artist name (case-insensitive)
  /// </summary>
  private static bool HasMatchingArtist(SpotifyArtist[] sourceArtists, SpotifyArtist[] targetArtists)
  {
    if (sourceArtists == null || targetArtists == null) return false;
    
    return sourceArtists.Any(sourceArtist => 
      targetArtists.Any(targetArtist => 
        string.Equals(sourceArtist.Name, targetArtist.Name, StringComparison.OrdinalIgnoreCase)
      )
    );
  }
}
