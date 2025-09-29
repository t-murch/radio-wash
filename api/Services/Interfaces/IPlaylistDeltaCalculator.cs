using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Spotify;

namespace RadioWash.Api.Services.Interfaces;

public class PlaylistDelta
{
    public List<string> TracksToAdd { get; set; } = new();
    public List<string> TracksToRemove { get; set; } = new();
    public List<SpotifyTrack> NewTracks { get; set; } = new();
    public List<string> DesiredTrackOrder { get; set; } = new();
}

public interface IPlaylistDeltaCalculator
{
    Task<PlaylistDelta> CalculateDeltaAsync(
        List<SpotifyTrack> sourceTracks,
        List<SpotifyTrack> targetTracks,
        List<TrackMapping> existingMappings);
}