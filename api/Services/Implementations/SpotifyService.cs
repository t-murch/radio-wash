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
  private readonly IUserProviderTokenService _tokenProvider; // CORRECT: Use the new token provider
  private readonly ILogger<SpotifyService> _logger;

  public SpotifyService(
      HttpClient httpClient,
      IOptions<SpotifySettings> spotifySettings,
      IUserProviderTokenService tokenProvider, // CORRECT: Inject the new service
      ILogger<SpotifyService> logger)
  {
    _httpClient = httpClient;
    _spotifySettings = spotifySettings.Value;
    _tokenProvider = tokenProvider;
    _logger = logger;
  }

  // A private helper to reduce boilerplate
  private async Task<HttpRequestMessage> CreateSpotifyRequestAsync(HttpMethod method, string url, string supabaseUserId)
  {
    var accessToken = await _tokenProvider.GetProviderAccessTokenAsync(supabaseUserId);
    var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    return request;
  }

  public async Task<SpotifyUserProfile> GetUserProfileAsync(string supabaseUserId)
  {
    var request = await CreateSpotifyRequestAsync(HttpMethod.Get, $"{_spotifySettings.ApiBaseUrl}/me", supabaseUserId);
    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();
    var jsonResponse = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<SpotifyUserProfile>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new Exception("Failed to deserialize user profile.");
  }

  public async Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(string supabaseUserId)
  {
    var playlists = new List<SpotifyPlaylist>();
    var url = $"{_spotifySettings.ApiBaseUrl}/me/playlists?limit=50";

    while (!string.IsNullOrEmpty(url))
    {
      var request = await CreateSpotifyRequestAsync(HttpMethod.Get, url, supabaseUserId);
      var response = await _httpClient.SendAsync(request);
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

  public async Task<IEnumerable<SpotifyTrack>> GetPlaylistTracksAsync(string supabaseUserId, string playlistId)
  {
    var tracks = new List<SpotifyTrack>();
    var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks?limit=100";

    while (!string.IsNullOrEmpty(url))
    {
      var request = await CreateSpotifyRequestAsync(HttpMethod.Get, url, supabaseUserId);
      var response = await _httpClient.SendAsync(request);
      response.EnsureSuccessStatusCode();

      var jsonResponse = await response.Content.ReadAsStringAsync();
      var tracksResponse = JsonSerializer.Deserialize<SpotifyPlaylistTracksResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

      if (tracksResponse?.Items == null) throw new Exception("Failed to deserialize tracks response.");

      // Filter out potential null tracks if the API ever returns them
      tracks.AddRange(tracksResponse.Items.Where(i => i.Track != null).Select(i => i.Track!));
      url = tracksResponse.Next;
    }
    return tracks;
  }

  public async Task<SpotifyPlaylist> CreatePlaylistAsync(string supabaseUserId, string name, string? description = null)
  {
    var userProfile = await GetUserProfileAsync(supabaseUserId);
    var url = $"{_spotifySettings.ApiBaseUrl}/users/{userProfile.Id}/playlists";
    var request = await CreateSpotifyRequestAsync(HttpMethod.Post, url, supabaseUserId);

    var payload = new { name, description = description ?? $"Clean version of {name} created by RadioWash", @public = false };
    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<SpotifyPlaylist>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new Exception("Failed to deserialize created playlist.");
  }

  public async Task AddTracksToPlaylistAsync(string supabaseUserId, string playlistId, IEnumerable<string> trackUris)
  {
    if (!trackUris.Any()) return;

    foreach (var uriChunk in trackUris.Chunk(100))
    {
      var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks";
      var request = await CreateSpotifyRequestAsync(HttpMethod.Post, url, supabaseUserId);
      var payload = new { uris = uriChunk };
      request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
      var response = await _httpClient.SendAsync(request);
      response.EnsureSuccessStatusCode();
    }
  }

  public async Task<SpotifyTrack?> FindCleanVersionAsync(string supabaseUserId, SpotifyTrack explicitTrack)
  {
    if (!explicitTrack.Explicit) return explicitTrack;

    var artists = string.Join(" ", explicitTrack.Artists.Select(a => a.Name));
    // Construct a search query that excludes "explicit" and looks for the same track name and artist.
    var query = $"{explicitTrack.Name} {artists} -tag:explicit";
    var encodedQuery = Uri.EscapeDataString(query);
    var url = $"{_spotifySettings.ApiBaseUrl}/search?q={encodedQuery}&type=track&limit=5";

    var request = await CreateSpotifyRequestAsync(HttpMethod.Get, url, supabaseUserId);
    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    var searchResponse = JsonSerializer.Deserialize<SpotifySearchResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    // Find the best non-explicit match
    return searchResponse?.Tracks?.Items?.FirstOrDefault(
        t => !t.Explicit && t.Name.Equals(explicitTrack.Name, StringComparison.OrdinalIgnoreCase)
    );
  }
}
