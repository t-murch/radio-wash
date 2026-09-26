namespace RadioWash.Api.Models.Music;

/// <summary>
/// Shared heuristics for deciding whether two catalog entries describe the same recording.
/// Used by both the cross-service <c>TrackMatcher</c> and the per-provider clean-version
/// search in the music-service adapters, which previously carried near-identical private
/// copies that were free to drift apart.
/// </summary>
public static class TrackMatching
{
  /// <summary>
  /// Clean edits trim profanity, not runtime; beyond this gap it's a different recording
  /// (remix, live take, extended edit).
  /// </summary>
  public const int DurationToleranceMs = 3000;

  // Clean editions are frequently listed as "Song (Clean)" / "Song [Clean]".
  private static readonly string[] CleanSuffixes = { "(clean)", "[clean]" };

  /// <summary>
  /// Compares track titles ignoring case and any trailing clean-edition marker.
  /// Null is treated as an empty title.
  /// </summary>
  public static bool NamesMatch(string? a, string? b) =>
    string.Equals(NormalizeName(a), NormalizeName(b), StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// Strips a trailing clean-edition marker so "Song (Clean)" compares equal to "Song".
  /// Null normalizes to an empty string.
  /// </summary>
  public static string NormalizeName(string? name)
  {
    var normalized = (name ?? string.Empty).Trim();
    foreach (var suffix in CleanSuffixes)
    {
      if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
      {
        normalized = normalized[..^suffix.Length].TrimEnd();
      }
    }
    return normalized;
  }

  /// <summary>
  /// Providers shape artist lists differently (some give one entry per artist; Apple: a single
  /// joined string), so overlap checks containment in both directions. A null or blank name on
  /// either side never overlaps: unknown metadata is no evidence of a match, and a blank
  /// string would otherwise be "contained" in every name.
  /// </summary>
  public static bool HasArtistOverlap(
    IReadOnlyList<MusicArtist> source,
    IReadOnlyList<MusicArtist> candidate) =>
    source.Any(s => candidate.Any(c => NamesOverlap(s.Name, c.Name)));

  /// <summary>
  /// Overload for providers that expose the candidate's artists as one joined string.
  /// </summary>
  public static bool HasArtistOverlap(
    IReadOnlyList<MusicArtist> source,
    string? candidateArtistName) =>
    source.Any(s => NamesOverlap(s.Name, candidateArtistName));

  private static bool NamesOverlap(string? a, string? b) =>
    !string.IsNullOrWhiteSpace(a) &&
    !string.IsNullOrWhiteSpace(b) &&
    (a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
     b.Contains(a, StringComparison.OrdinalIgnoreCase));

  /// <summary>
  /// Unknown durations never disqualify a candidate — only a known, large gap does.
  /// </summary>
  public static bool DurationsCompatible(int? sourceMs, int? candidateMs) =>
    sourceMs is null || candidateMs is null ||
    Math.Abs(sourceMs.Value - candidateMs.Value) <= DurationToleranceMs;
}
