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
  private readonly HttpClient _httpClient;
  private readonly SpotifySettings _spotifySettings;
  private readonly IMusicTokenService _musicTokenService;
  private readonly ILogger<SpotifyService> _logger;

  public SpotifyService(
      HttpClient httpClient,
      IOptions<SpotifySettings> spotifySettings,
      IMusicTokenService musicTokenService,
      ILogger<SpotifyService> logger)
  {
    _httpClient = httpClient;
    _spotifySettings = spotifySettings.Value;
    _musicTokenService = musicTokenService;
    _logger = logger;
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
  private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, int userId, int maxRetries = 3)
  {
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
      try
      {
        var response = await _httpClient.SendAsync(request);
        
        // If unauthorized, try to refresh token and retry once more
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt == 1)
        {
          _logger.LogWarning("Spotify API returned 401, attempting token refresh for user {UserId}", userId);
          
          var refreshed = await _musicTokenService.RefreshTokensAsync(userId, "spotify");
          if (refreshed)
          {
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
            continue; // Retry with new token
          }
        }
        
        return response;
      }
      catch (HttpRequestException ex) when (attempt < maxRetries)
      {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
        _logger.LogWarning(ex, "HTTP request failed (attempt {Attempt}/{MaxRetries}), retrying after {Delay}s", 
          attempt, maxRetries, delay.TotalSeconds);
        await Task.Delay(delay);
        
        // Recreate request for retry (HttpRequestMessage can only be sent once)
        request = new HttpRequestMessage(request.Method, request.RequestUri)
        {
          Content = request.Content
        };
        foreach (var header in request.Headers)
        {
          request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
      }
    }
    
    throw new HttpRequestException($"Failed to complete Spotify API request after {maxRetries} attempts");
  }

  public async Task<SpotifyUserProfile> GetUserProfileAsync(int userId)
  {
    var request = await CreateSpotifyRequestAsync(HttpMethod.Get, $"{_spotifySettings.ApiBaseUrl}/me", userId);
    var response = await SendWithRetryAsync(request, userId);
    response.EnsureSuccessStatusCode();
    var jsonResponse = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<SpotifyUserProfile>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new Exception("Failed to deserialize user profile.");
  }

  public async Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(int userId)
  {
    var playlists = new List<SpotifyPlaylist>();
    var url = $"{_spotifySettings.ApiBaseUrl}/me/playlists?limit=50";

    while (!string.IsNullOrEmpty(url))
    {
      var request = await CreateSpotifyRequestAsync(HttpMethod.Get, url, userId);
      var response = await SendWithRetryAsync(request, userId);
      response.EnsureSuccessStatusCode();

      var jsonResponse = await response.Content.ReadAsStringAsync();
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

  public async Task<IEnumerable<SpotifyTrack>> GetPlaylistTracksAsync(int userId, string playlistId)
  {
    var tracks = new List<SpotifyTrack>();
    var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks?limit=100";

    while (!string.IsNullOrEmpty(url))
    {
      var request = await CreateSpotifyRequestAsync(HttpMethod.Get, url, userId);
      var response = await SendWithRetryAsync(request, userId);
      response.EnsureSuccessStatusCode();

      var jsonResponse = await response.Content.ReadAsStringAsync();
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

  public async Task<SpotifyPlaylist> CreatePlaylistAsync(int userId, string name, string? description = null)
  {
    var userProfile = await GetUserProfileAsync(userId);
    var url = $"{_spotifySettings.ApiBaseUrl}/users/{userProfile.Id}/playlists";
    var request = await CreateSpotifyRequestAsync(HttpMethod.Post, url, userId);

    var payload = new { name, description = description ?? $"Clean version of {name} created by RadioWash", @public = false };
    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    var response = await SendWithRetryAsync(request, userId);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<SpotifyPlaylist>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new Exception("Failed to deserialize created playlist.");
  }

  public async Task AddTracksToPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackUris)
  {
    if (!trackUris.Any()) return;

    foreach (var uriChunk in trackUris.Chunk(100))
    {
      var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks";
      var request = await CreateSpotifyRequestAsync(HttpMethod.Post, url, userId);
      var payload = new { uris = uriChunk };
      request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
      var response = await SendWithRetryAsync(request, userId);
      response.EnsureSuccessStatusCode();
    }
  }

  public async Task RemoveTracksFromPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackUris)
  {
    if (!trackUris.Any()) return;

    foreach (var uriChunk in trackUris.Chunk(100))
    {
      var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks";
      var request = await CreateSpotifyRequestAsync(HttpMethod.Delete, url, userId);
      var tracks = uriChunk.Select(uri => new { uri }).ToArray();
      var payload = new { tracks };
      request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
      var response = await SendWithRetryAsync(request, userId);
      response.EnsureSuccessStatusCode();
    }
  }

  public async Task<SpotifyTrack?> FindCleanVersionAsync(int userId, SpotifyTrack explicitTrack)
  {
    if (!explicitTrack.Explicit) return explicitTrack;

    var artists = string.Join(" ", explicitTrack.Artists.Select(a => a.Name));
    // Construct a search query that excludes "explicit" and looks for the same track name and artist.
    var query = $"{explicitTrack.Name} {artists} -tag:explicit";
    var encodedQuery = Uri.EscapeDataString(query);
    var url = $"{_spotifySettings.ApiBaseUrl}/search?q={encodedQuery}&type=track&limit=5";

    var request = await CreateSpotifyRequestAsync(HttpMethod.Get, url, userId);
    var response = await SendWithRetryAsync(request, userId);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    var searchResponse = JsonSerializer.Deserialize<SpotifySearchResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    // Find the best non-explicit match
    return searchResponse?.Tracks?.Items?.FirstOrDefault(
        t => !t.Explicit && t.Name.Equals(explicitTrack.Name, StringComparison.OrdinalIgnoreCase)
    );
  }
}
