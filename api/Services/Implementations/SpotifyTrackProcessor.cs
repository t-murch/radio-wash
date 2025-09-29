using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class SpotifyTrackProcessor : ITrackProcessor
{
  private readonly ISpotifyService _spotifyService;
  private readonly ILogger<SpotifyTrackProcessor> _logger;

  public SpotifyTrackProcessor(ISpotifyService spotifyService, ILogger<SpotifyTrackProcessor> logger)
  {
    _spotifyService = spotifyService;
    _logger = logger;
  }

  public async Task<TrackMapping> ProcessTrackAsync(int userId, int jobId, SpotifyTrack track)
  {
    var cleanVersion = await _spotifyService.FindCleanVersionAsync(userId, track);

    return new TrackMapping
    {
      JobId = jobId,
      SourceTrackId = track.Id,
      SourceTrackName = track.Name ?? "Unknown",
      SourceArtistName = FormatArtists(track.Artists),
      IsExplicit = track.Explicit,
      HasCleanMatch = cleanVersion != null,
      TargetTrackId = cleanVersion?.Id,
      TargetTrackName = cleanVersion?.Name,
      TargetArtistName = cleanVersion != null ? FormatArtists(cleanVersion.Artists) : null
    };
  }

  public async Task<SpotifyTrack?> FindCleanVersionAsync(SpotifyTrack explicitTrack)
  {
    // This is a simplified implementation - for a real implementation,
    // you would need to use the Spotify search API to find clean versions
    // For now, we'll just return null to indicate no clean version found
    _logger.LogInformation("Searching for clean version of track {TrackId} ({TrackName})",
        explicitTrack.Id, explicitTrack.Name);

    // TODO: Implement actual clean version search logic
    return await Task.FromResult<SpotifyTrack?>(null);
  }

  private string FormatArtists(SpotifyArtist[]? artists)
  {
    return artists?.Length > 0
        ? string.Join(", ", artists.Select(a => a.Name))
        : "Unknown";
  }
}
