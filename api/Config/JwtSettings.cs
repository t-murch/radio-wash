namespace RadioWash.Api.Configuration;

public class JwtSettings
{
  public const string SectionName = "Jwt";

  public string Secret { get; set; } = null!;
  public string Issuer { get; set; } = null!;
  public string Audience { get; set; } = null!;
  public int ExpirationInMinutes { get; set; }
  public string CookieName { get; set; } = "rw-auth";
  public string RefreshCookieName { get; set; } = "rw-refresh";
  public int RefreshExpirationDays { get; set; } = 7;
}
