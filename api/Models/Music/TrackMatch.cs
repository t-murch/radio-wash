namespace RadioWash.Api.Models.Music;

/// <summary>
/// Methods a cross-catalog match can be found by; persisted on TrackMapping.MatchMethod.
/// </summary>
public static class MatchMethods
{
  public const string Isrc = "isrc";
  public const string IsrcClean = "isrc-clean";
  public const string Search = "search";
  public const string SearchClean = "search-clean";
  public const string None = "none";
}

/// <summary>
/// Outcome of matching one source track into a target platform's catalog.
/// <see cref="Target"/> is null when no acceptable match exists.
/// </summary>
public sealed record TrackMatch(MusicTrack Source, MusicTrack? Target, string Method);
