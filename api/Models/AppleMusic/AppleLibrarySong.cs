using System.Text.Json.Serialization;

namespace RadioWash.Api.Models.AppleMusic;

/// <summary>
/// A song inside a user's library playlist. Library ids ("i.XXXX") are meaningless outside
/// the user's library; the catalog linkage (playParams.catalogId or the catalog
/// relationship) is what maps the track to the public catalog. Personal uploads and
/// region-unavailable tracks have no catalog linkage.
/// </summary>
public class AppleLibrarySong
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("attributes")]
  public AppleLibrarySongAttributes Attributes { get; set; } = null!;

  [JsonPropertyName("relationships")]
  public AppleLibrarySongRelationships? Relationships { get; set; }

  public string? CatalogId =>
      Attributes.PlayParams?.CatalogId
      ?? Relationships?.Catalog?.Data?.FirstOrDefault()?.Id;
}

public class AppleLibrarySongAttributes
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

  [JsonPropertyName("playParams")]
  public ApplePlayParams? PlayParams { get; set; }
}

public class AppleLibrarySongRelationships
{
  [JsonPropertyName("catalog")]
  public AppleRelationship? Catalog { get; set; }
}

public class AppleRelationship
{
  [JsonPropertyName("data")]
  public AppleResourceRef[]? Data { get; set; }
}

public class AppleResourceRef
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = null!;

  [JsonPropertyName("type")]
  public string Type { get; set; } = null!;
}
