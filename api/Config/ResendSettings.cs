namespace RadioWash.Api.Configuration;

public class ResendSettings
{
  public const string SectionName = "Resend";

  public string? ApiKey { get; set; }

  // Overridden only by tests, which point it at a local fake.
  public string BaseUrl { get; set; } = "https://api.resend.com";
}
