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
    private readonly IProgressBroadcastService _progressService;
    private readonly ILogger<CleanPlaylistJobProcessor> _logger;

    public CleanPlaylistJobProcessor(
        IUnitOfWork unitOfWork,
        IPlaylistCleanerFactory cleanerFactory,
        IProgressBroadcastService progressService,
        ILogger<CleanPlaylistJobProcessor> logger)
    {
        _unitOfWork = unitOfWork;
        _cleanerFactory = cleanerFactory;
        _progressService = progressService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2)]
    public async Task ProcessJobAsync(int jobId)
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

            // Use factory to get appropriate cleaner (currently only Spotify)
            var cleaner = _cleanerFactory.CreateCleaner("spotify");
            var result = await cleaner.CleanPlaylistAsync(job, user);

            await CompleteJob(job, result);
            await _progressService.BroadcastJobCompleted(jobId, 
                $"Processed {result.ProcessedTracks} tracks, matched {result.MatchedTracks} clean versions");
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