using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.AppleMusic;

public class AppleLibraryPlaylist
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("attributes")]
  public AppleLibraryPlaylistAttributes Attributes { get; set; } = null!;
}

public class AppleLibraryPlaylistAttributes
{
  [JsonPropertyName("name")]
  public string Name { get; set; } = null!;

  [JsonPropertyName("description")]
  public AppleDescription? Description { get; set; }

  [JsonPropertyName("artwork")]
  public AppleArtwork? Artwork { get; set; }

  [JsonPropertyName("canEdit")]
  public bool CanEdit { get; set; }

  [JsonPropertyName("hasCatalog")]
  public bool HasCatalog { get; set; }

  [JsonPropertyName("playParams")]
  public ApplePlayParams? PlayParams { get; set; }
}

public class AppleDescription
{
  [JsonPropertyName("standard")]
  public string? Standard { get; set; }
}

public class AppleArtwork
{
  // Template URL containing {w}/{h} placeholders, e.g. ".../{w}x{h}bb.jpg"
  [JsonPropertyName("url")]
  public string? Url { get; set; }
}

public class ApplePlayParams
{
  [JsonPropertyName("id")]
  public string? Id { get; set; }

  [JsonPropertyName("catalogId")]
  public string? CatalogId { get; set; }

  // Public catalog id of a library playlist ("pl.u-..."), when the playlist has one.
  [JsonPropertyName("globalId")]
  public string? GlobalId { get; set; }

  [JsonPropertyName("isLibrary")]
  public bool IsLibrary { get; set; }
}
