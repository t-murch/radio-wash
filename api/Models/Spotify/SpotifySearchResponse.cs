using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.Spotify;

public class SpotifySearchResponse
{
  [JsonPropertyName("tracks")]
  public SpotifyTracks Tracks { get; set; } = null!;
}

public class SpotifyTracks
{
  [JsonPropertyName("items")]
  public SpotifyTrack[] Items { get; set; } = null!;

  [JsonPropertyName("total")]
  public int Total { get; set; }

  [JsonPropertyName("limit")]
  public int Limit { get; set; }

  [JsonPropertyName("offset")]
  public int Offset { get; set; }

  [JsonPropertyName("next")]
  public string? Next { get; set; }
}
