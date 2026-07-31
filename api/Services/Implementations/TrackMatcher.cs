using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class TrackMatcher : ITrackMatcher
{
  private const int SearchLimit = 10;

  private readonly ILogger<TrackMatcher> _logger;

  public TrackMatcher(ILogger<TrackMatcher> logger)
  {
    _logger = logger;
  }

  public Task<IReadOnlyDictionary<string, MusicTrack>> PrefetchByIsrcAsync(
      int userId,
      IMusicService target,
      IReadOnlyCollection<string> isrcs,
      CancellationToken cancellationToken)
  {
    return target.GetTracksByIsrcAsync(userId, isrcs, cancellationToken);
  }

  public async Task<TrackMatch> MatchAsync(
      int userId,
      IMusicService target,
      MusicTrack source,
      IReadOnlyDictionary<string, MusicTrack> isrcIndex,
      bool preferClean,
      CancellationToken cancellationToken)
  {
    // 1. ISRC hit — same recording on the target platform.
    if (source.Isrc is not null && isrcIndex.TryGetValue(source.Isrc, out var isrcHit))
    {
      if (!preferClean || !isrcHit.IsExplicit)
      {
        return new TrackMatch(source, isrcHit, MatchMethods.Isrc);
      }

      // Explicit hit under the clean toggle: swap for the platform's clean version. No
      // clean version means unmatched — mirroring the same-service clean semantics rather
      // than silently copying explicit content the user asked to filter.
      var cleanVersion = await target.FindCleanVersionAsync(userId, isrcHit, cancellationToken);
      return cleanVersion is not null && !cleanVersion.IsExplicit
        ? new TrackMatch(source, cleanVersion, MatchMethods.IsrcClean)
        : new TrackMatch(source, null, MatchMethods.None);
    }

    // 2. Search fallback.
    var artists = string.Join(" ", source.Artists.Select(a => a.Name));
    var candidates = await target.SearchTracksAsync(userId, $"{source.Name} {artists}", SearchLimit, cancellationToken);
    var plausible = candidates.Where(c => IsPlausibleMatch(source, c)).ToList();

    if (preferClean && source.IsExplicit)
    {
      var clean = plausible.FirstOrDefault(c => !c.IsExplicit);
      return clean is not null
        ? new TrackMatch(source, clean, MatchMethods.SearchClean)
        : new TrackMatch(source, null, MatchMethods.None);
    }

    // Faithful copy: prefer the candidate matching the source's explicitness, then any
    // plausible candidate.
    var best = plausible.FirstOrDefault(c => c.IsExplicit == source.IsExplicit)
        ?? plausible.FirstOrDefault();
    if (best is null)
    {
      _logger.LogDebug("No {Target} match for '{Track}' by {Artists}", target.ProviderName, source.Name, artists);
      return new TrackMatch(source, null, MatchMethods.None);
    }

    return new TrackMatch(source, best, MatchMethods.Search);
  }

  private static bool IsPlausibleMatch(MusicTrack source, MusicTrack candidate) =>
    TrackMatching.NamesMatch(source.Name, candidate.Name) &&
    TrackMatching.HasArtistOverlap(source.Artists, candidate.Artists) &&
    TrackMatching.DurationsCompatible(source.DurationMs, candidate.DurationMs);
}
