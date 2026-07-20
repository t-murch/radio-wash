using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.AppleMusic;

public class AppleCatalogSong
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("attributes")]
  public AppleCatalogSongAttributes Attributes { get; set; } = null!;
}

public class AppleCatalogSongAttributes
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = null!;

  [JsonPropertyName("artistName")]
  public string ArtistName { get; set; } = null!;

  [JsonPropertyName("albumName")]
  public string? AlbumName { get; set; }

  // "explicit" or "clean"; absent when the song has no rating.
  [JsonPropertyName("contentRating")]
  public string? ContentRating { get; set; }

  [JsonPropertyName("durationInMillis")]
  public int? DurationInMillis { get; set; }

  [JsonPropertyName("isrc")]
  public string? Isrc { get; set; }

  [JsonPropertyName("url")]
  public string? Url { get; set; }
}

public class AppleSearchResponse
{
  [JsonPropertyName("results")]
  public AppleSearchResults? Results { get; set; }
}

public class AppleSearchResults
{
  [JsonPropertyName("songs")]
  public AppleSearchSongs? Songs { get; set; }
}

public class AppleSearchSongs
{
  [JsonPropertyName("data")]
  public AppleCatalogSong[] Data { get; set; } = Array.Empty<AppleCatalogSong>();
}
