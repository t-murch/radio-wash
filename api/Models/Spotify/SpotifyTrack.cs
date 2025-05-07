using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.Spotify;

public class SpotifyPlaylistTracksResponse
{
  [JsonPropertyName("items")]
  public SpotifyPlaylistTrack[] Items { get; set; } = null!;

  [JsonPropertyName("total")]
  public int Total { get; set; }

  [JsonPropertyName("limit")]
  public int Limit { get; set; }

  [JsonPropertyName("offset")]
  public int Offset { get; set; }

  [JsonPropertyName("next")]
  public string? Next { get; set; }
}

public class SpotifyPlaylistTrack
{
  [JsonPropertyName("track")]
  public SpotifyTrack Track { get; set; } = null!;

  [JsonPropertyName("added_at")]
  public string AddedAt { get; set; } = null!;
}

public class SpotifyTrack
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("name")]
  public string Name { get; set; } = null!;

  [JsonPropertyName("artists")]
  public SpotifyArtist[] Artists { get; set; } = null!;

  [JsonPropertyName("album")]
  public SpotifyAlbum Album { get; set; } = null!;

  [JsonPropertyName("explicit")]
  public bool Explicit { get; set; }

  [JsonPropertyName("popularity")]
  public int Popularity { get; set; }

  [JsonPropertyName("uri")]
  public string Uri { get; set; } = null!;
}

public class SpotifyArtist
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("name")]
  public string Name { get; set; } = null!;
}

public class SpotifyAlbum
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("name")]
  public string Name { get; set; } = null!;

  [JsonPropertyName("images")]
  public SpotifyImage[]? Images { get; set; }
}
