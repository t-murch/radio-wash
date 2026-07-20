namespace RadioWash.Api.Configuration;

public class AppleMusicSettings
{
  public const string SectionName = "AppleMusic";

  public string TeamId { get; set; } = null!;
  public string KeyId { get; set; } = null!;

  // The MusicKit private key (.p8) can be supplied three ways; first non-empty wins.
  // PrivateKeyBase64 exists because Azure app settings mangle multiline values.
  public string? PrivateKey { get; set; }
  public string? PrivateKeyBase64 { get; set; }
  public string? PrivateKeyPath { get; set; }

  public string ApiBaseUrl { get; set; } = "https://api.music.apple.com/v1";

  // Apple caps developer tokens at 180 days; regenerate comfortably before that.
  public int DeveloperTokenLifetimeDays { get; set; } = 150;

  // Music User Tokens are long-lived (~6 months) and expose no expiry signal;
  // we assume this lifetime when storing them so reconnect prompts fire in time.
  public int UserTokenAssumedLifetimeDays { get; set; } = 150;

  public string DefaultStorefront { get; set; } = "us";
}
