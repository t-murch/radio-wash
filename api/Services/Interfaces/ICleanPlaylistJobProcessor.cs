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
  /// processor translates <see cref="IJobCancellationToken.ShutdownToken"/> into a standard
  /// <see cref="System.Threading.CancellationToken"/> and threads it through the cleaner.</param>
  Task ProcessJobAsync(int jobId, IJobCancellationToken cancellationToken);
}
