namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Separate interface for job processing (used by background workers)
/// </summary>
public interface ICleanPlaylistJobProcessor
{
  Task ProcessJobAsync(int jobId);
}
