using Hangfire;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Shared handling for Hangfire's IJobCancellationToken quirks, used by the cleaner and the
/// copier. JobCancellationToken.Null (the sentinel used at enqueue time and in direct unit
/// tests) has no backing ShutdownToken, so accessing it throws NullReferenceException; these
/// helpers treat it as a non-cancellable token. Real Hangfire runtime tokens expose a valid
/// ShutdownToken for cooperative server-shutdown cancellation.
/// </summary>
internal static class HangfireCancellationHelper
{
  public static CancellationToken ResolveShutdownToken(IJobCancellationToken token)
  {
    try
    {
      return token.ShutdownToken;
    }
    catch (NullReferenceException)
    {
      return CancellationToken.None;
    }
  }

  public static void ThrowIfCancellationRequested(IJobCancellationToken token)
  {
    try
    {
      token.ThrowIfCancellationRequested();
    }
    catch (NullReferenceException)
    {
      // JobCancellationToken.Null also throws here; treat it as a non-cancellable sentinel
      // for direct unit tests and enqueue-time placeholders.
    }
  }
}
