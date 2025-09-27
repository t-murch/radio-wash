using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Spotify;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Processes individual tracks
/// </summary>
public interface ITrackProcessor
{
    Task<TrackMapping> ProcessTrackAsync(int userId, int jobId, SpotifyTrack track);
}