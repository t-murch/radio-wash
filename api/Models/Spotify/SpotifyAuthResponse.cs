using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.Spotify;

public class SpotifyAuthResponse
{
  [JsonPropertyName("access_token")]
  public string AccessToken { get; set; } = null!;

  [JsonPropertyName("token_type")]
  public string TokenType { get; set; } = null!;

  [JsonPropertyName("scope")]
  public string Scope { get; set; } = null!;

  [JsonPropertyName("expires_in")]
  public int ExpiresIn { get; set; }

  [JsonPropertyName("refresh_token")]
  public string RefreshToken { get; set; } = null!;
}
