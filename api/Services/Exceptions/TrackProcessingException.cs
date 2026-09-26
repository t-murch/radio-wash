using RadioWash.Api.Models.Music;

namespace RadioWash.Api.Services.Exceptions;

// Thrown by PlaylistCleaner when processing a single track fails. The job processor logs the
// job-level failure once; without this wrapper that log names only the job, and a bad track
// in a multi-thousand-track playlist is unidentifiable from the error report alone.
public class TrackProcessingException : Exception
{
  public int JobId { get; }
  public int Position { get; }
  public string SourceTrackId { get; }
  public string SourceTrackName { get; }

  public TrackProcessingException(int jobId, int position, MusicTrack track, Exception inner)
    : base($"Failed processing track {position} '{track.Name}' ({track.Id}) for job {jobId}: {inner.Message}", inner)
  {
    JobId = jobId;
    Position = position;
    SourceTrackId = track.Id;
    SourceTrackName = track.Name;
  }
}
