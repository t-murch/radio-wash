using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class PlaylistDeltaCalculator : IPlaylistDeltaCalculator
{
  private readonly ILogger<PlaylistDeltaCalculator> _logger;

  public PlaylistDeltaCalculator(ILogger<PlaylistDeltaCalculator> logger)
  {
    _logger = logger;
  }

  public async Task<PlaylistDelta> CalculateDeltaAsync(
      List<MusicTrack> sourceTracks,
      List<MusicTrack> targetTracks,
      List<TrackMapping> existingMappings)
  {
    _logger.LogInformation("Calculating playlist delta for {SourceCount} source tracks, {TargetCount} target tracks, {MappingCount} existing mappings",
        sourceTracks.Count, targetTracks.Count, existingMappings.Count);

    var delta = new PlaylistDelta();

    // Mappings are keyed by provider catalog song id: one row per song, even when the
    // song appears in the playlist several times (rows written before write-side dedup
    // existed can still repeat a SourceTrackId). Duplicates are collapsed here rather
    // than rejected; a mapping carrying a usable clean match wins so a matchless
    // duplicate cannot shadow it.
    var mappingsBySourceId = new Dictionary<string, TrackMapping>();
    foreach (var candidate in existingMappings)
    {
      if (!mappingsBySourceId.TryGetValue(candidate.SourceTrackId, out var current) ||
          (!HasUsableMatch(current) && HasUsableMatch(candidate)))
      {
        mappingsBySourceId[candidate.SourceTrackId] = candidate;
      }
    }

    if (mappingsBySourceId.Count < existingMappings.Count)
    {
      _logger.LogWarning("Collapsed {DuplicateCount} duplicate track mapping(s) sharing a SourceTrackId",
          existingMappings.Count - mappingsBySourceId.Count);
    }

    // Find tracks to add. Occurrence counts are compared per clean version so the target
    // mirrors the source's duplicates: a song the source holds twice keeps two copies of
    // its clean version in the target. Extra copies already in the target are left alone
    // (removal is not supported by the provider API).
    var targetTrackCounts = targetTracks
        .GroupBy(t => t.Id)
        .ToDictionary(g => g.Key, g => g.Count());
    var desiredSoFar = new Dictionary<string, int>();
    foreach (var sourceTrack in sourceTracks)
    {
      // Check if we have a clean version mapping
      if (mappingsBySourceId.TryGetValue(sourceTrack.Id, out var mapping))
      {
        if (HasUsableMatch(mapping))
        {
          var targetId = mapping.TargetTrackId!;
          var desiredCount = desiredSoFar.GetValueOrDefault(targetId) + 1;
          desiredSoFar[targetId] = desiredCount;
          if (desiredCount > targetTrackCounts.GetValueOrDefault(targetId))
          {
            delta.TracksToAdd.Add(targetId);
            _logger.LogDebug("Track to add: {SourceTrack} -> {TargetTrack}", sourceTrack.Name, targetId);
          }
        }
      }
      else
      {
        // New track not in original mappings; kept per occurrence so duplicates carry over
        delta.NewTracks.Add(sourceTrack);
        _logger.LogDebug("New track discovered: {TrackName} ({TrackId})", sourceTrack.Name, sourceTrack.Id);
      }
    }

    // Find tracks to remove (in target but not in source). Distinct source tracks can
    // share one clean version, so group by target id: a target track is only removable
    // when every source track mapped to it has left the playlist.
    var sourceTrackIds = new HashSet<string>(sourceTracks.Select(t => t.Id));
    var mappingsByTargetId = existingMappings
        .Where(HasUsableMatch)
        .ToLookup(m => m.TargetTrackId!);

    foreach (var targetTrack in targetTracks)
    {
      if (mappingsByTargetId.Contains(targetTrack.Id) &&
          mappingsByTargetId[targetTrack.Id].All(m => !sourceTrackIds.Contains(m.SourceTrackId)))
      {
        delta.TracksToRemove.Add(targetTrack.Id);
        _logger.LogDebug("Track to remove: {TargetTrack} (no mapped source track exists anymore)",
            targetTrack.Name);
      }
    }

    // Calculate desired track order based on source playlist order
    delta.DesiredTrackOrder = CalculateDesiredOrder(sourceTracks, mappingsBySourceId);

    _logger.LogInformation("Delta calculation complete: {TracksToAdd} to add, {TracksToRemove} to remove, {NewTracks} new tracks",
        delta.TracksToAdd.Count, delta.TracksToRemove.Count, delta.NewTracks.Count);

    return await Task.FromResult(delta);
  }

  private static bool HasUsableMatch(TrackMapping mapping) =>
      mapping.HasCleanMatch && !string.IsNullOrEmpty(mapping.TargetTrackId);

  private List<string> CalculateDesiredOrder(List<MusicTrack> sourceTracks, Dictionary<string, TrackMapping> mappingsBySourceId)
  {
    var desiredOrder = new List<string>();

    foreach (var sourceTrack in sourceTracks)
    {
      if (mappingsBySourceId.TryGetValue(sourceTrack.Id, out var mapping) && HasUsableMatch(mapping))
      {
        desiredOrder.Add(mapping.TargetTrackId!);
      }
    }

    return desiredOrder;
  }
}
