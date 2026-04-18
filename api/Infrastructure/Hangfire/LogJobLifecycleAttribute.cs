using global::Hangfire.Common;
using global::Hangfire.Server;
using global::Hangfire.States;
using global::Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace RadioWash.Api.Infrastructure.Hangfire;

/// <summary>
/// Global Hangfire job filter that emits a structured log event for each lifecycle
/// transition a background job goes through: Performing, Performed, Succeeded, Failed,
/// Deleted. Registered once via GlobalJobFilters; any job Hangfire runs gets this visibility
/// without per-job opt-in.
///
/// The lifecycle events are the only Hangfire-side observability this codebase currently has.
/// They give on-call a single structured-log handle to watch when diagnosing stuck jobs or
/// verifying a deploy didn't break the queue, without having to rely on Sentry alone.
/// </summary>
public class LogJobLifecycleAttribute : JobFilterAttribute, IServerFilter, IElectStateFilter, IApplyStateFilter
{
  private readonly ILogger<LogJobLifecycleAttribute> _logger;

  public LogJobLifecycleAttribute(ILogger<LogJobLifecycleAttribute> logger)
  {
    _logger = logger;
  }

  public void OnPerforming(PerformingContext context)
  {
    _logger.LogInformation(
      "Hangfire job {JobId} starting: {JobType}.{JobMethod}",
      context.BackgroundJob.Id,
      context.BackgroundJob.Job.Type.Name,
      context.BackgroundJob.Job.Method.Name);
  }

  public void OnPerformed(PerformedContext context)
  {
    if (context.Exception != null)
    {
      _logger.LogError(
        context.Exception,
        "Hangfire job {JobId} threw {ExceptionType}: {JobType}.{JobMethod}",
        context.BackgroundJob.Id,
        context.Exception.GetType().Name,
        context.BackgroundJob.Job.Type.Name,
        context.BackgroundJob.Job.Method.Name);
    }
    else
    {
      _logger.LogInformation(
        "Hangfire job {JobId} finished: {JobType}.{JobMethod}",
        context.BackgroundJob.Id,
        context.BackgroundJob.Job.Type.Name,
        context.BackgroundJob.Job.Method.Name);
    }
  }

  public void OnStateElection(ElectStateContext context)
  {
    if (context.CandidateState is FailedState failed)
    {
      _logger.LogError(
        failed.Exception,
        "Hangfire job {JobId} entering Failed state: {Reason}",
        context.BackgroundJob.Id,
        failed.Reason);
    }
  }

  public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
  {
    _logger.LogDebug(
      "Hangfire job {JobId} state {OldState} -> {NewState}",
      context.BackgroundJob.Id,
      context.OldStateName,
      context.NewState.Name);
  }

  public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
  {
    // Intentionally empty. Hangfire requires this method on IApplyStateFilter but we don't
    // need to log un-applied transitions — they're an internal detail of retry reapplication.
  }
}
