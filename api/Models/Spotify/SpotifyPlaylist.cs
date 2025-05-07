using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.Spotify;

public class SpotifyPlaylistsResponse
{
  [JsonPropertyName("items")]
  public SpotifyPlaylist[] Items { get; set; } = null!;

  [JsonPropertyName("total")]
  public int Total { get; set; }

  [JsonPropertyName("limit")]
  public int Limit { get; set; }

  [JsonPropertyName("offset")]
  public int Offset { get; set; }

  [JsonPropertyName("next")]
  public string? Next { get; set; }
}

public class SpotifyPlaylist
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("name")]
  public string Name { get; set; } = null!;

  [JsonPropertyName("description")]
  public string? Description { get; set; }

  [JsonPropertyName("images")]
  public SpotifyImage[]? Images { get; set; }

  [JsonPropertyName("tracks")]
  public SpotifyPlaylistTracksRef Tracks { get; set; } = null!;

  [JsonPropertyName("owner")]
  public SpotifyUser Owner { get; set; } = null!;
}

public class SpotifyPlaylistTracksRef
{
  [JsonPropertyName("href")]
  public string Href { get; set; } = null!;

  [JsonPropertyName("total")]
  public int Total { get; set; }
}

public class SpotifyUser
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("display_name")]
  public string? DisplayName { get; set; }
}
