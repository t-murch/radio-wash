using Hangfire;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Services;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Implements the service responsible for creating and processing playlist cleaning jobs.
/// This service follows the Single Responsibility Principle, focusing only on the command
/// and processing aspects of a job, not on querying data for the API.
/// </summary>
public class CleanPlaylistService : ICleanPlaylistService
{
  private readonly RadioWashDbContext _dbContext;
  private readonly ISpotifyService _spotifyService;
  private readonly ILogger<CleanPlaylistService> _logger;
  private readonly IBackgroundJobClient _backgroundJobClient;

  public CleanPlaylistService(
      RadioWashDbContext dbContext,
      ISpotifyService spotifyService,
      ILogger<CleanPlaylistService> logger,
      IBackgroundJobClient backgroundJobClient)
  {
    _dbContext = dbContext;
    _spotifyService = spotifyService;
    _logger = logger;
    _backgroundJobClient = backgroundJobClient;
  }

  /// <summary>
  /// Creates a new job to clean a playlist and enqueues it for background processing.
  /// </summary>
  /// <param name="userId">The internal integer ID of the user.</param>
  /// <param name="jobDto">The DTO containing the source playlist ID and optional new name.</param>
  /// <returns>A DTO representing the newly created job.</returns>
  public async Task<CleanPlaylistJobDto> CreateJobAsync(int userId, CreateCleanPlaylistJobDto jobDto)
  {
    using var transaction = await _dbContext.Database.BeginTransactionAsync();
    try
    {
      var user = await _dbContext.Users.FindAsync(userId);
      if (user == null)
      {
        throw new KeyNotFoundException("User not found");
      }

      // Fetch all of the user's playlists from Spotify.
      var allPlaylists = await _spotifyService.GetUserPlaylistsAsync(user.Id);
      // Find the specific playlist the user wants to clean from the results.
      var sourcePlaylist = allPlaylists.FirstOrDefault(p => p.Id == jobDto.SourcePlaylistId);

      if (sourcePlaylist == null)
      {
        throw new KeyNotFoundException("Source playlist not found on Spotify or user does not have access.");
      }

      var job = new CleanPlaylistJob
      {
        UserId = userId,
        SourcePlaylistId = sourcePlaylist.Id,
        SourcePlaylistName = sourcePlaylist.Name,
        TargetPlaylistName = string.IsNullOrWhiteSpace(jobDto.TargetPlaylistName)
              ? $"Clean - {sourcePlaylist.Name}"
              : jobDto.TargetPlaylistName,
        Status = JobStatus.Pending,
        TotalTracks = sourcePlaylist.TrackCount
      };

      await _dbContext.CleanPlaylistJobs.AddAsync(job);
      await _dbContext.SaveChangesAsync();

      // Enqueue the job for processing in the background with Hangfire.
      _backgroundJobClient.Enqueue<ICleanPlaylistService>(service => service.ProcessJobAsync(job.Id));

      await transaction.CommitAsync();

      _logger.LogInformation("Created and enqueued job {JobId} for user {UserId}", job.Id, userId);

      // Map the new job entity to a DTO to return to the controller.
      return new CleanPlaylistJobDto
      {
        Id = job.Id,
        SourcePlaylistId = job.SourcePlaylistId,
        SourcePlaylistName = job.SourcePlaylistName,
        TargetPlaylistName = job.TargetPlaylistName,
        Status = job.Status,
        TotalTracks = job.TotalTracks,
        CurrentBatch = job.CurrentBatch,
        BatchSize = job.BatchSize,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt
      };
    }
    catch
    {
      await transaction.RollbackAsync();
      throw;
    }
  }

  /// <summary>
  /// The background process that finds clean versions of tracks and creates a new playlist.
  /// Uses smart progress reporting with percentage-based batching for optimal progress tracking.
  /// This method is intended to be called by Hangfire and should not be called directly from controllers.
  /// </summary>
  /// <param name="jobId">The ID of the job to process.</param>
  [AutomaticRetry(Attempts = 2)]
  public async Task ProcessJobAsync(int jobId)
  {
    var job = await _dbContext.CleanPlaylistJobs.FindAsync(jobId);
    if (job == null)
    {
      _logger.LogError("Job {JobId} not found for processing.", jobId);
      return;
    }

    try
    {
      var user = await _dbContext.Users.FindAsync(job.UserId);
      if (user == null) throw new InvalidOperationException($"User for job {jobId} not found.");

      // Fetch all tracks and initialize smart progress reporter
      var allTracks = await _spotifyService.GetPlaylistTracksAsync(user.Id, job.SourcePlaylistId);
      var totalTracks = allTracks.Count();
      var progressReporter = new SmartProgressReporter(totalTracks);

      // Update job with total tracks and batch information
      using var initTransaction = await _dbContext.Database.BeginTransactionAsync();
      try
      {
        job.Status = JobStatus.Processing;
        job.TotalTracks = totalTracks;
        job.BatchSize = progressReporter.BatchSize;
        job.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await initTransaction.CommitAsync();

        // Initialize progress tracking
        var initialUpdate = progressReporter.CreateUpdate(0);
        job.CurrentBatch = initialUpdate.CurrentBatch;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to initialize job {JobId} for processing", jobId);
        await initTransaction.RollbackAsync();
        return;
      }

      var cleanTrackUris = new List<string>();
      var trackBatch = new List<TrackMapping>();
      var tracksProcessed = 0;

      foreach (var track in allTracks)
      {
        tracksProcessed++;

        // Process individual track
        var cleanVersion = await _spotifyService.FindCleanVersionAsync(user.Id, track);
        var mapping = new TrackMapping
        {
          JobId = jobId,
          SourceTrackId = track.Id,
          SourceTrackName = track.Name,
          SourceArtistName = string.Join(", ", track.Artists.Select(a => a.Name)),
          IsExplicit = track.Explicit,
          HasCleanMatch = cleanVersion != null,
          TargetTrackId = cleanVersion?.Id,
          TargetTrackName = cleanVersion?.Name,
          TargetArtistName = cleanVersion != null ? string.Join(", ", cleanVersion.Artists.Select(a => a.Name)) : null
        };

        trackBatch.Add(mapping);

        if (cleanVersion != null)
        {
          job.MatchedTracks++;
          cleanTrackUris.Add(cleanVersion.Uri);
        }

        // Check if we should report progress
        if (progressReporter.ShouldReportProgress(tracksProcessed))
        {
          var update = progressReporter.CreateUpdate(tracksProcessed, track.Name);

          // Persist to database every 10% (or at completion)
          if (update.Progress % 10 == 0 || tracksProcessed == totalTracks)
          {
            using var batchTransaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
              // Save track mappings batch
              if (trackBatch.Any())
              {
                await _dbContext.TrackMappings.AddRangeAsync(trackBatch);
                trackBatch.Clear();
              }

              // Update job progress
              job.ProcessedTracks = tracksProcessed;
              job.CurrentBatch = update.CurrentBatch;
              job.UpdatedAt = DateTime.UtcNow;
              await _dbContext.SaveChangesAsync();
              await batchTransaction.CommitAsync();

              _logger.LogDebug("Progress saved for job {JobId}: {ProcessedTracks}/{TotalTracks} ({Progress}%)",
                             jobId, tracksProcessed, totalTracks, update.Progress);
            }
            catch (Exception ex)
            {
              _logger.LogError(ex, "Failed to save progress for job {JobId} at track {ProcessedTracks}", jobId, tracksProcessed);
              await batchTransaction.RollbackAsync();
              throw;
            }
          }
        }
      }

      // Final transaction for playlist creation and job completion
      using var completionTransaction = await _dbContext.Database.BeginTransactionAsync();
      try
      {
        // Save any remaining track mappings
        if (trackBatch.Any())
        {
          await _dbContext.TrackMappings.AddRangeAsync(trackBatch);
        }

        // Create new playlist
        var newPlaylist = await _spotifyService.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, "Cleaned by RadioWash.");
        job.TargetPlaylistId = newPlaylist.Id;

        // Add clean tracks to playlist
        if (cleanTrackUris.Any())
        {
          await _spotifyService.AddTracksToPlaylistAsync(user.Id, newPlaylist.Id, cleanTrackUris);
        }

        // Complete the job
        job.Status = JobStatus.Completed;
        job.ProcessedTracks = totalTracks;
        job.CurrentBatch = "Completed";
        job.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await completionTransaction.CommitAsync();

        _logger.LogInformation("Successfully completed job {JobId}. Processed {ProcessedTracks} tracks, matched {MatchedTracks} clean versions",
                             jobId, job.ProcessedTracks, job.MatchedTracks);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to complete job {JobId} during playlist creation", jobId);
        await completionTransaction.RollbackAsync();
        throw;
      }
    }
    catch (Exception ex)
    {
      // Handle any errors with atomic rollback for job failure status
      using var errorTransaction = await _dbContext.Database.BeginTransactionAsync();
      try
      {
        _logger.LogError(ex, "Error processing job {JobId}", jobId);

        // Reload job to get latest state in case it was modified in a failed transaction
        await _dbContext.Entry(job).ReloadAsync();

        job.Status = JobStatus.Failed;
        job.ErrorMessage = ex.Message;
        job.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await errorTransaction.CommitAsync();
      }
      catch (Exception errorEx)
      {
        _logger.LogError(errorEx, "Failed to update job {JobId} status to Failed", jobId);
        await errorTransaction.RollbackAsync();
        throw ex; // Re-throw the original exception
      }
    }
  }
}
