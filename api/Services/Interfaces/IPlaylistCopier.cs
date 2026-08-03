using Hangfire;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Executes cross-service copy jobs: reads the source playlist from job.Provider, matches
/// each track into job.TargetProvider's catalog (optionally swapping explicit tracks for
/// clean versions), and creates the playlist on the target platform.
/// </summary>
public interface IPlaylistCopier
{
  Task<PlaylistCleaningResult> CopyPlaylistAsync(
    CleanPlaylistJob job,
    User user,
    IJobCancellationToken? cancellationToken = null);
}
