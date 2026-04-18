using Hangfire;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Separate interface for job processing (used by background workers)
/// </summary>
public interface ICleanPlaylistJobProcessor
{
  /// <summary>
  /// Processes a clean-playlist job end to end.
  /// </summary>
  /// <param name="jobId">The persisted job identifier.</param>
  /// <param name="cancellationToken">Hangfire cancellation token; at enqueue time callers pass
  /// <c>JobCancellationToken.Null</c> and Hangfire substitutes a real token at run time. The
  /// processor passes the Hangfire token through so the cleaner can observe both
  /// per-job aborts via <see cref="IJobCancellationToken.ThrowIfCancellationRequested"/> and
  /// server shutdown via <see cref="IJobCancellationToken.ShutdownToken"/>.</param>
  Task ProcessJobAsync(int jobId, IJobCancellationToken cancellationToken);
}
