namespace RadioWash.Api.Models.Domain;

public class TrackMapping
{
  public int Id { get; set; }
  public int JobId { get; set; }
  public string SourceTrackId { get; set; } = null!;
  public string SourceTrackName { get; set; } = null!;
  public string SourceArtistName { get; set; } = null!;
  public bool IsExplicit { get; set; }
  public string? TargetTrackId { get; set; }
  public string? TargetTrackName { get; set; }
  public string? TargetArtistName { get; set; }
  public bool HasCleanMatch { get; set; }

  /// <summary>Source track's ISRC when known — the cross-catalog identity used for matching.</summary>
  public string? Isrc { get; set; }

  /// <summary>
  /// How the target track was found on a copy job: "isrc", "isrc-clean", "search",
  /// "search-clean", or "none" (see <c>MatchMethods</c>). Null for same-service clean jobs,
  /// which have only one match path and so nothing to disambiguate. Diagnostics for the
  /// job-details UI, which renders this only for copy jobs.
  /// </summary>
  public string? MatchMethod { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  public CleanPlaylistJob Job { get; set; } = null!;
}
