using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.AppleMusic;

public class AppleStorefrontResponse
{
  [JsonPropertyName("data")]
  public AppleStorefront[] Data { get; set; } = Array.Empty<AppleStorefront>();
}

public class AppleStorefront
{
  // Storefront code, e.g. "us"
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;
}
