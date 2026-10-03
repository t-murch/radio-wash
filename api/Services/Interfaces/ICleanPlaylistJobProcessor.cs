using Hangfire;
using Hangfire.Server;
using RadioWash.Api.Infrastructure.Hangfire;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Separate interface for job processing (used by background workers)
/// </summary>
public interface ICleanPlaylistJobProcessor
{
  /// <summary>
  /// Processes a clean-playlist job end to end.
  /// </summary>
  /// <remarks>
  /// The retry filter lives here, not on the implementation: jobs are enqueued through this
  /// interface and Hangfire reads filter attributes from the enqueued type's method. Retries
  /// resume rather than restart, because the cleaner reuses the track mappings a failed
  /// attempt already persisted.
  /// </remarks>
  /// <param name="jobId">The persisted job identifier.</param>
  /// <param name="performContext">Hangfire's context for the running attempt; callers pass
  /// <c>null</c> at enqueue time and Hangfire substitutes the real one. The processor reads the
  /// retry count from it to tell whether a transient failure will be retried.</param>
  /// <param name="cancellationToken">Hangfire cancellation token; at enqueue time callers pass
  /// <c>JobCancellationToken.Null</c> and Hangfire substitutes a real token at run time. The
  /// processor passes the Hangfire token through so the cleaner can observe both
  /// per-job aborts via <see cref="IJobCancellationToken.ThrowIfCancellationRequested"/> and
  /// server shutdown via <see cref="IJobCancellationToken.ShutdownToken"/>.</param>
  [AutomaticRetry(
    Attempts = JobRetryPolicy.Attempts,
    DelaysInSeconds = new[] { JobRetryPolicy.FirstRetryDelaySeconds, JobRetryPolicy.SecondRetryDelaySeconds })]
  Task ProcessJobAsync(int jobId, PerformContext? performContext, IJobCancellationToken cancellationToken);

  /// <summary>
  /// Legacy signature from before the processor took a <c>PerformContext</c>. Hangfire matches
  /// stored jobs by exact parameter types, so jobs enqueued before that deploy need it to load.
  /// Runs as a final attempt. Safe to remove once no job with this signature remains in
  /// Hangfire storage.
  /// </summary>
  [AutomaticRetry(Attempts = 0)]
  Task ProcessJobAsync(int jobId, IJobCancellationToken cancellationToken);
}
