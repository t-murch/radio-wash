using Hangfire;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

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

    _logger.LogInformation("Created and enqueued job {JobId} for user {UserId}", job.Id, userId);

    // Map the new job entity to a DTO to return to the controller.
    return new CleanPlaylistJobDto
    {
      Id = job.Id,
      SourcePlaylistId = job.SourcePlaylistId,
      SourcePlaylistName = job.SourcePlaylistName,
      Status = job.Status,
      TotalTracks = job.TotalTracks,
      CreatedAt = job.CreatedAt,
      UpdatedAt = job.UpdatedAt
    };
  }

  /// <summary>
  /// The background process that finds clean versions of tracks and creates a new playlist.
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
      job.Status = JobStatus.Processing;
      job.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync(); // This update notifies the client via Supabase Realtime

      var user = await _dbContext.Users.FindAsync(job.UserId);
      if (user == null) throw new InvalidOperationException($"User for job {jobId} not found.");

      var allTracks = await _spotifyService.GetPlaylistTracksAsync(user.Id, job.SourcePlaylistId);
      job.TotalTracks = allTracks.Count();
      var cleanTrackUris = new List<string>();

      foreach (var track in allTracks)
      {
        job.ProcessedTracks++;
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

        await _dbContext.TrackMappings.AddAsync(mapping);

        if (cleanVersion != null)
        {
          job.MatchedTracks++;
          cleanTrackUris.Add(cleanVersion.Uri);
        }

        // Periodically save progress to the database, which triggers a real-time update
        if (job.ProcessedTracks % 10 == 0 || job.ProcessedTracks == job.TotalTracks)
        {
          job.UpdatedAt = DateTime.UtcNow;
          await _dbContext.SaveChangesAsync();
        }
      }

      var newPlaylist = await _spotifyService.CreatePlaylistAsync(user.Id, job.TargetPlaylistName, "Cleaned by RadioWash.");
      job.TargetPlaylistId = newPlaylist.Id;

      if (cleanTrackUris.Any())
      {
        await _spotifyService.AddTracksToPlaylistAsync(user.Id, newPlaylist.Id, cleanTrackUris);
      }

      job.Status = JobStatus.Completed;
      job.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync(); // Final update notifies client of completion
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing job {JobId}", jobId);
      job.Status = JobStatus.Failed;
      job.ErrorMessage = ex.Message;
      job.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync(); // Notifies client of failure
    }
  }
}
