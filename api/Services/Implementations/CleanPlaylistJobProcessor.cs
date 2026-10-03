using Hangfire;
using Hangfire.Server;
using RadioWash.Api.Infrastructure.Hangfire;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Separate job processor following SRP
/// </summary>
public class CleanPlaylistJobProcessor : ICleanPlaylistJobProcessor
{
  private readonly IUnitOfWork _unitOfWork;
  private readonly IPlaylistCleanerFactory _cleanerFactory;
  private readonly IPlaylistCopier _playlistCopier;
  private readonly IProgressBroadcastService _progressService;
  private readonly ILogger<CleanPlaylistJobProcessor> _logger;

  public CleanPlaylistJobProcessor(
      IUnitOfWork unitOfWork,
      IPlaylistCleanerFactory cleanerFactory,
      IPlaylistCopier playlistCopier,
      IProgressBroadcastService progressService,
      ILogger<CleanPlaylistJobProcessor> logger)
  {
    _unitOfWork = unitOfWork;
    _cleanerFactory = cleanerFactory;
    _playlistCopier = playlistCopier;
    _progressService = progressService;
    _logger = logger;
  }

  public Task ProcessJobAsync(int jobId, IJobCancellationToken cancellationToken) =>
      ProcessJobAsync(jobId, null, cancellationToken);

  // Retry policy (attempts and delays) is declared on ICleanPlaylistJobProcessor, where
  // Hangfire reads it. A transient failure with attempts left is rethrown so Hangfire
  // reschedules the job; anything else, or the last attempt, marks the job failed and returns.
  public async Task ProcessJobAsync(int jobId, PerformContext? performContext, IJobCancellationToken cancellationToken)
  {
    var job = await _unitOfWork.Jobs.GetByIdAsync(jobId);
    if (job == null)
    {
      _logger.LogError("Job {JobId} not found", jobId);
      return;
    }

    try
    {
      await UpdateJobStatus(job, JobStatus.Processing);

      var user = await _unitOfWork.Users.GetByIdAsync(job.UserId)
          ?? throw new InvalidOperationException($"User {job.UserId} not found");

      // Copy jobs bridge two providers through the copier; clean jobs route to the
      // provider's cleaner. Unknown providers throw before any API call is made.
      var result = job.JobType == JobTypes.Copy
          ? await _playlistCopier.CopyPlaylistAsync(job, user, cancellationToken)
          : await _cleanerFactory.CreateCleaner(job.Provider).CleanPlaylistAsync(job, user, cancellationToken);

      await CompleteJob(job, result);
      var completionMessage = job.JobType == JobTypes.Copy
          ? $"Copied {result.MatchedTracks} of {result.ProcessedTracks} tracks to {job.TargetProvider}"
          : $"Processed {result.ProcessedTracks} tracks, matched {result.MatchedTracks} clean versions";
      await _progressService.BroadcastJobCompleted(jobId, completionMessage);
    }
    catch (Exception ex)
    {
      var retryCount = GetRetryCount(performContext, jobId);
      if (retryCount < JobRetryPolicy.Attempts && JobRetryPolicy.IsTransient(ex))
      {
        // Warning, not Error: a retry that succeeds is not an incident. Hangfire's own
        // AutomaticRetry logs the terminal error if every attempt fails.
        var delay = JobRetryPolicy.DelayBeforeRetry(retryCount!.Value);
        _logger.LogWarning(ex,
          "Job {JobId} hit a transient failure on attempt {Attempt} of {MaxAttempts}; retrying in {Delay}s",
          jobId, retryCount + 1, JobRetryPolicy.Attempts + 1, delay.TotalSeconds);
        await RecordPendingRetry(job, delay);
        throw;
      }

      _logger.LogError(ex, "Failed to process job {JobId}", jobId);
      await HandleJobFailure(jobId, ex);
    }
  }

  /// <summary>
  /// Retries already used, or null when unknown (no Hangfire context, as with the legacy
  /// signature, or the storage read failed). Unknown is treated as the last attempt so the
  /// job is always marked failed rather than left in Processing by a retry that never comes.
  /// </summary>
  private int? GetRetryCount(PerformContext? performContext, int jobId)
  {
    if (performContext == null) return null;
    try
    {
      return performContext.GetJobParameter<int>(JobRetryPolicy.RetryCountParameter);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Could not read Hangfire retry count for job {JobId}; treating as final attempt", jobId);
      return null;
    }
  }

  private async Task RecordPendingRetry(CleanPlaylistJob job, TimeSpan delay)
  {
    // The job stays Processing so the UI keeps it live; the batch label explains the pause.
    // `job` is the entity UpdateJobStatus attached to the shared DbContext, so its
    // ProcessedTracks reflects the cleaner's last persisted batch.
    try
    {
      await _unitOfWork.Jobs.UpdateProgressAsync(
          job.Id,
          job.ProcessedTracks,
          $"Temporary error from the music service, retrying in {delay.TotalSeconds:0}s");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to record pending retry for job {JobId}", job.Id);
    }
  }

  private async Task UpdateJobStatus(CleanPlaylistJob job, string status)
  {
    job.Status = status;
    await _unitOfWork.Jobs.UpdateAsync(job);
    await _unitOfWork.SaveChangesAsync();
  }

  private async Task CompleteJob(CleanPlaylistJob job, PlaylistCleaningResult result)
  {
    job.Status = JobStatus.Completed;
    job.ProcessedTracks = result.ProcessedTracks;
    job.MatchedTracks = result.MatchedTracks;
    job.TargetPlaylistId = result.TargetPlaylistId;
    job.CurrentBatch = "Completed";

    await _unitOfWork.Jobs.UpdateAsync(job);
    await _unitOfWork.SaveChangesAsync();
  }

  private async Task HandleJobFailure(int jobId, Exception ex)
  {
    try
    {
      await _unitOfWork.Jobs.UpdateErrorAsync(jobId, ex.Message);
      await _unitOfWork.SaveChangesAsync();
      await _progressService.BroadcastJobFailed(jobId, ex.Message);
    }
    catch (Exception innerEx)
    {
      _logger.LogError(innerEx, "Failed to update job {JobId} error status", jobId);
    }
  }
}
