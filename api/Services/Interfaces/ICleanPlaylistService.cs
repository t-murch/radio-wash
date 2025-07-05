using RadioWash.Api.Models.DTO;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Defines the service responsible for creating and processing playlist cleaning jobs.
/// </summary>
public interface ICleanPlaylistService
{
  /// <summary>
  /// Creates a new job to clean a playlist and enqueues it for background processing.
  /// </summary>
  Task<CleanPlaylistJobDto> CreateJobAsync(int userId, CreateCleanPlaylistJobDto jobDto);

  /// <summary>
  /// The background process that finds clean versions of tracks and creates a new playlist.
  /// This method is intended to be called by Hangfire.
  /// </summary>
  Task ProcessJobAsync(int jobId);
}
