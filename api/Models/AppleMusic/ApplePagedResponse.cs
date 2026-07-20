using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.AppleMusic;

/// <summary>
/// Envelope for Apple Music collection endpoints. <c>next</c> is a relative path
/// (e.g. "/v1/me/library/playlists?offset=25") that must be resolved against the API host.
/// </summary>
public class ApplePagedResponse<T>
{
  [JsonPropertyName("data")]
  public T[] Data { get; set; } = Array.Empty<T>();

  [JsonPropertyName("next")]
  public string? Next { get; set; }

  [JsonPropertyName("meta")]
  public AppleMeta? Meta { get; set; }
}

public class AppleMeta
{
  [JsonPropertyName("total")]
  public int? Total { get; set; }
}
