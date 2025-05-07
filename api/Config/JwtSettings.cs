namespace RadioWash.Api.Configuration;

public class JwtSettings
{
  public const string SectionName = "Jwt";

  public string Secret { get; set; } = null!;
  public string Issuer { get; set; } = null!;
  public string Audience { get; set; } = null!;
  public int ExpirationInMinutes { get; set; }
}
