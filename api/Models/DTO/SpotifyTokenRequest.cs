namespace RadioWash.Api.Models.DTO;

public class SpotifyTokenRequest
{
  public string AccessToken { get; set; } = string.Empty;
  public string RefreshToken { get; set; } = string.Empty;
  public DateTime ExpiresAt { get; set; }
}
