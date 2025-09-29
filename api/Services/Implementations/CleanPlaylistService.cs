using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Main service handling job creation and coordination
/// Follows SRP by focusing only on job management
/// </summary>
public class CleanPlaylistService : ICleanPlaylistService
{
  private readonly IUnitOfWork _unitOfWork;
  private readonly ISpotifyService _spotifyService;
  private readonly IJobOrchestrator _jobOrchestrator;
  private readonly ILogger<CleanPlaylistService> _logger;

  public CleanPlaylistService(
      IUnitOfWork unitOfWork,
      ISpotifyService spotifyService,
      IJobOrchestrator jobOrchestrator,
      ILogger<CleanPlaylistService> logger)
  {
    _unitOfWork = unitOfWork;
    _spotifyService = spotifyService;
    _jobOrchestrator = jobOrchestrator;
    _logger = logger;
  }

  public async Task<CleanPlaylistJobDto> CreateJobAsync(int userId, CreateCleanPlaylistJobDto jobDto)
  {
    await _unitOfWork.BeginTransactionAsync();
    try
    {
      var user = await _unitOfWork.Users.GetByIdAsync(userId)
        ?? throw new KeyNotFoundException($"User {userId} not found");

      var sourcePlaylist = await ValidateAndGetPlaylistAsync(user.Id, jobDto.SourcePlaylistId);
      var job = CreateJob(userId, sourcePlaylist, jobDto.TargetPlaylistName);
      
      await _unitOfWork.Jobs.CreateAsync(job);
      await _unitOfWork.SaveChangesAsync();

      var hangfireJobId = await _jobOrchestrator.EnqueueJobAsync(job.Id);
      _logger.LogInformation("Created job {JobId} with Hangfire ID {HangfireId}", job.Id, hangfireJobId);

      await _unitOfWork.CommitTransactionAsync();
      
      return MapToDto(job);
    }
    catch
    {
      await _unitOfWork.RollbackTransactionAsync();
      throw;
    }
  }

  public async Task<JobProgress> GetJobProgressAsync(int jobId)
  {
    var job = await _unitOfWork.Jobs.GetByIdAsync(jobId)
      ?? throw new KeyNotFoundException($"Job {jobId} not found");

    return new JobProgress(
      job.ProcessedTracks,
      job.TotalTracks,
      job.CurrentBatch ?? "Not started",
      job.MatchedTracks);
  }

  private async Task<PlaylistDto> ValidateAndGetPlaylistAsync(int userId, string playlistId)
  {
    var playlists = await _spotifyService.GetUserPlaylistsAsync(userId);
    var playlist = playlists.FirstOrDefault(p => p.Id == playlistId)
      ?? throw new KeyNotFoundException($"Playlist {playlistId} not found or user lacks access");
    
    return playlist;
  }

  private CleanPlaylistJob CreateJob(int userId, PlaylistDto sourcePlaylist, string? targetName)
  {
    return new CleanPlaylistJob
    {
      UserId = userId,
      SourcePlaylistId = sourcePlaylist.Id,
      SourcePlaylistName = sourcePlaylist.Name,
      TargetPlaylistName = string.IsNullOrWhiteSpace(targetName)
        ? $"Clean - {sourcePlaylist.Name}"
        : targetName,
      Status = JobStatus.Pending,
      TotalTracks = sourcePlaylist.TrackCount
    };
  }

  private CleanPlaylistJobDto MapToDto(CleanPlaylistJob job)
  {
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
}
