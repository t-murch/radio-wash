using System.Data.Common;
using System.Net;

namespace RadioWash.Api.Infrastructure.Hangfire;

/// <summary>
/// Retry budget and failure classification for playlist jobs. The constants feed the
/// <c>[AutomaticRetry]</c> on <c>ICleanPlaylistJobProcessor</c>, and the processor reads the same
/// numbers to decide whether Hangfire will run the job again, so the two cannot drift apart.
/// </summary>
public static class JobRetryPolicy
{
  public const int Attempts = 2;
  public const int FirstRetryDelaySeconds = 30;
  public const int SecondRetryDelaySeconds = 120;

  /// <summary>
  /// Job parameter <c>AutomaticRetryAttribute</c> sets before scheduling each retry: absent (0)
  /// on the first run, 1 on the first retry, and so on.
  /// </summary>
  public const string RetryCountParameter = "RetryCount";

  private static readonly int[] DelaysInSeconds = { FirstRetryDelaySeconds, SecondRetryDelaySeconds };

  /// <summary>Delay Hangfire waits before the retry that follows the run with this retry count.</summary>
  public static TimeSpan DelayBeforeRetry(int retryCount) =>
    TimeSpan.FromSeconds(DelaysInSeconds[Math.Clamp(retryCount, 0, DelaysInSeconds.Length - 1)]);

  /// <summary>
  /// True when the failure is likely to clear on its own: provider 5xx/429, network failures,
  /// timeouts, and transient database errors. Walks the inner-exception chain because the
  /// cleaner wraps per-track failures in <c>TrackProcessingException</c>. Auth failures,
  /// missing data, bugs, and cancellation are permanent: retrying them only delays the error.
  /// </summary>
  public static bool IsTransient(Exception exception)
  {
    for (var ex = exception; ex != null; ex = ex.InnerException)
    {
      switch (ex)
      {
        // No status means the request never got a response (connection failure), or the
        // provider client already exhausted its own retries.
        case HttpRequestException http:
          return http.StatusCode is null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
        // HttpClient.Timeout surfaces as TaskCanceledException wrapping TimeoutException.
        case TimeoutException:
          return true;
        case DbException { IsTransient: true }:
          return true;
      }
    }
    return false;
  }
}
