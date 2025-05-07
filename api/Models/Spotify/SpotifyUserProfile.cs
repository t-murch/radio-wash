using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.Spotify;

public class SpotifyUserProfile
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("display_name")]
  public string DisplayName { get; set; } = null!;

  [JsonPropertyName("email")]
  public string Email { get; set; } = null!;

  [JsonPropertyName("images")]
  public SpotifyImage[]? Images { get; set; }
}

public class SpotifyImage
{
  [JsonPropertyName("url")]
  public string Url { get; set; } = null!;

  [JsonPropertyName("height")]
  public int? Height { get; set; }

  [JsonPropertyName("width")]
  public int? Width { get; set; }
}

