namespace RadioWash.Api.Configuration;

public class SpotifySettings
{
  public const string SectionName = "Spotify";

  public string ClientId { get; set; } = null!;
  public string ClientSecret { get; set; } = null!;
  public string RedirectUri { get; set; } = null!;
  public string[] Scopes { get; set; } = null!;
  public string AuthUrl => "https://accounts.spotify.com/authorize";
  public string TokenUrl => "https://accounts.spotify.com/api/token";
  public string ApiBaseUrl => "https://api.spotify.com/v1";
}
