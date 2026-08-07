using Hangfire;
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

  // Exponential retry: 30s, 120s. Better than Hangfire's immediate-retry default when the
  // most likely cause of failure is a provider hiccup or a token-refresh race — a little
  // back-off gives the upstream time to recover before we spend the whole attempt budget.
  [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 30, 120 })]
  public async Task ProcessJobAsync(int jobId, IJobCancellationToken cancellationToken)
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
      _logger.LogError(ex, "Failed to process job {JobId}", jobId);
      await HandleJobFailure(jobId, ex);
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
