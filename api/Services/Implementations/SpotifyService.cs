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
  private readonly ITokenService _tokenService;

  public SpotifyService(
      HttpClient httpClient,
      IOptions<SpotifySettings> spotifySettings,
      ITokenService tokenService)
  {
    _httpClient = httpClient;
    _spotifySettings = spotifySettings.Value;
    _tokenService = tokenService;
  }

  public async Task<SpotifyUserProfile> GetUserProfileAsync(string accessToken)
  {
    var request = new HttpRequestMessage(HttpMethod.Get, $"{_spotifySettings.ApiBaseUrl}/me");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    var userProfile = JsonSerializer.Deserialize<SpotifyUserProfile>(
        jsonResponse,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    if (userProfile == null)
    {
      throw new Exception("Failed to deserialize user profile");
    }

    return userProfile;
  }

  public async Task<List<PlaylistDto>> GetUserPlaylistsAsync(int userId)
  {
    var accessToken = await _tokenService.GetAccessTokenAsync(userId);
    var playlists = new List<SpotifyPlaylist>();
    var limit = 50;
    var offset = 0;
    var hasMore = true;

    while (hasMore)
    {
      var url = $"{_spotifySettings.ApiBaseUrl}/me/playlists?limit={limit}&offset={offset}";
      var request = new HttpRequestMessage(HttpMethod.Get, url);
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

      var response = await _httpClient.SendAsync(request);
      response.EnsureSuccessStatusCode();

      var jsonResponse = await response.Content.ReadAsStringAsync();
      var playlistsResponse = JsonSerializer.Deserialize<SpotifyPlaylistsResponse>(
          jsonResponse,
          new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
      );

      if (playlistsResponse == null)
      {
        throw new Exception("Failed to deserialize playlists response");
      }

      playlists.AddRange(playlistsResponse.Items);

      offset += limit;
      hasMore = !string.IsNullOrEmpty(playlistsResponse.Next);
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
    }).ToList();
  }

  public async Task<List<SpotifyPlaylistTrack>> GetPlaylistTracksAsync(int userId, string playlistId)
  {
    var accessToken = await _tokenService.GetAccessTokenAsync(userId);
    var tracks = new List<SpotifyPlaylistTrack>();
    var limit = 100;
    var offset = 0;
    var hasMore = true;

    while (hasMore)
    {
      var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks?limit={limit}&offset={offset}";
      var request = new HttpRequestMessage(HttpMethod.Get, url);
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

      var response = await _httpClient.SendAsync(request);
      response.EnsureSuccessStatusCode();

      var jsonResponse = await response.Content.ReadAsStringAsync();
      var tracksResponse = JsonSerializer.Deserialize<SpotifyPlaylistTracksResponse>(
          jsonResponse,
          new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
      );

      if (tracksResponse == null)
      {
        throw new Exception("Failed to deserialize tracks response");
      }

      tracks.AddRange(tracksResponse.Items);

      offset += limit;
      hasMore = !string.IsNullOrEmpty(tracksResponse.Next);
    }

    return tracks;
  }

  public async Task<string> CreatePlaylistAsync(int userId, string name, string? description = null)
  {
    var accessToken = await _tokenService.GetAccessTokenAsync(userId);
    var userProfile = await GetUserProfileAsync(accessToken);

    var url = $"{_spotifySettings.ApiBaseUrl}/users/{userProfile.Id}/playlists";
    var request = new HttpRequestMessage(HttpMethod.Post, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    var payload = new
    {
      name,
      description = description ?? $"Clean version of {name} created by RadioWash",
      @public = false
    };

    var jsonPayload = JsonSerializer.Serialize(payload);
    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    var playlistResponse = JsonSerializer.Deserialize<SpotifyPlaylist>(
        jsonResponse,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    if (playlistResponse == null)
    {
      throw new Exception("Failed to deserialize playlist response");
    }

    return playlistResponse.Id;
  }

  public async Task AddTracksToPlaylistAsync(int userId, string playlistId, List<string> trackUris)
  {
    if (!trackUris.Any())
    {
      return;
    }

    var accessToken = await _tokenService.GetAccessTokenAsync(userId);

    // Spotify limits to 100 tracks per request, so we need to chunk
    foreach (var uriChunk in trackUris.Chunk(100))
    {
      var url = $"{_spotifySettings.ApiBaseUrl}/playlists/{playlistId}/tracks";
      var request = new HttpRequestMessage(HttpMethod.Post, url);
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

      var payload = new
      {
        uris = uriChunk.ToArray()
      };

      var jsonPayload = JsonSerializer.Serialize(payload);
      request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

      var response = await _httpClient.SendAsync(request);
      response.EnsureSuccessStatusCode();
    }
  }

  public async Task<List<SpotifyTrack>> SearchTracksAsync(int userId, string query, int limit = 5)
  {
    var accessToken = await _tokenService.GetAccessTokenAsync(userId);

    var encodedQuery = Uri.EscapeDataString(query);
    var url = $"{_spotifySettings.ApiBaseUrl}/search?q={encodedQuery}&type=track&limit={limit}";

    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    var searchResponse = JsonSerializer.Deserialize<SpotifySearchResponse>(
        jsonResponse,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    if (searchResponse == null || searchResponse.Tracks?.Items == null)
    {
      throw new Exception("Failed to deserialize search response");
    }

    return searchResponse.Tracks.Items.ToList();
  }
}
