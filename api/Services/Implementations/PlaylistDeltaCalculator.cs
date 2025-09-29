using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Spotify;
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
      List<SpotifyTrack> sourceTracks,
      List<SpotifyTrack> targetTracks,
      List<TrackMapping> existingMappings)
  {
    _logger.LogInformation("Calculating playlist delta for {SourceCount} source tracks, {TargetCount} target tracks, {MappingCount} existing mappings",
        sourceTracks.Count, targetTracks.Count, existingMappings.Count);

    var delta = new PlaylistDelta();

    // Create lookup dictionaries for efficiency
    var targetTrackIds = new HashSet<string>(targetTracks.Select(t => t.Id));
    var mappingsBySourceId = existingMappings.ToDictionary(m => m.SourceTrackId);

    // Find tracks to add (in source but not in target)
    foreach (var sourceTrack in sourceTracks)
    {
      // Check if we have a clean version mapping
      if (mappingsBySourceId.TryGetValue(sourceTrack.Id, out var mapping))
      {
        if (mapping.HasCleanMatch && !string.IsNullOrEmpty(mapping.TargetTrackId) && !targetTrackIds.Contains(mapping.TargetTrackId))
        {
          delta.TracksToAdd.Add(mapping.TargetTrackId);
          _logger.LogDebug("Track to add: {SourceTrack} -> {TargetTrack}", sourceTrack.Name, mapping.TargetTrackId);
        }
      }
      else
      {
        // New track not in original mappings
        delta.NewTracks.Add(sourceTrack);
        _logger.LogDebug("New track discovered: {TrackName} ({TrackId})", sourceTrack.Name, sourceTrack.Id);
      }
    }

    // Find tracks to remove (in target but not in source)
    var sourceTrackIds = new HashSet<string>(sourceTracks.Select(t => t.Id));
    var mappingsByTargetId = existingMappings
        .Where(m => m.HasCleanMatch && !string.IsNullOrEmpty(m.TargetTrackId))
        .ToDictionary(m => m.TargetTrackId!);

    foreach (var targetTrack in targetTracks)
    {
      if (mappingsByTargetId.TryGetValue(targetTrack.Id, out var mapping))
      {
        if (!sourceTrackIds.Contains(mapping.SourceTrackId))
        {
          delta.TracksToRemove.Add(targetTrack.Id);
          _logger.LogDebug("Track to remove: {TargetTrack} (source {SourceTrack} no longer exists)",
              targetTrack.Name, mapping.SourceTrackId);
        }
      }
    }

    // Calculate desired track order based on source playlist order
    delta.DesiredTrackOrder = CalculateDesiredOrder(sourceTracks, mappingsBySourceId);

    _logger.LogInformation("Delta calculation complete: {TracksToAdd} to add, {TracksToRemove} to remove, {NewTracks} new tracks",
        delta.TracksToAdd.Count, delta.TracksToRemove.Count, delta.NewTracks.Count);

    return await Task.FromResult(delta);
  }

  private List<string> CalculateDesiredOrder(List<SpotifyTrack> sourceTracks, Dictionary<string, TrackMapping> mappingsBySourceId)
  {
    var desiredOrder = new List<string>();

    foreach (var sourceTrack in sourceTracks)
    {
      if (mappingsBySourceId.TryGetValue(sourceTrack.Id, out var mapping) &&
          mapping.HasCleanMatch &&
          !string.IsNullOrEmpty(mapping.TargetTrackId))
      {
        desiredOrder.Add(mapping.TargetTrackId);
      }
    }

    return desiredOrder;
  }
}
