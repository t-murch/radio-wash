using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Main service handling job creation and coordination
/// Follows SRP by focusing only on job management
/// </summary>
public class CleanPlaylistService : ICleanPlaylistService
{
  private readonly IUnitOfWork _unitOfWork;
  private readonly IMusicServiceFactory _musicServiceFactory;
  private readonly IMusicTokenService _musicTokenService;
  private readonly IJobOrchestrator _jobOrchestrator;
  private readonly ILogger<CleanPlaylistService> _logger;

  public CleanPlaylistService(
      IUnitOfWork unitOfWork,
      IMusicServiceFactory musicServiceFactory,
      IMusicTokenService musicTokenService,
      IJobOrchestrator jobOrchestrator,
      ILogger<CleanPlaylistService> logger)
  {
    _unitOfWork = unitOfWork;
    _musicServiceFactory = musicServiceFactory;
    _musicTokenService = musicTokenService;
    _jobOrchestrator = jobOrchestrator;
    _logger = logger;
  }

  public async Task<CleanPlaylistJobDto> CreateJobAsync(int userId, CreateCleanPlaylistJobDto jobDto)
  {
    var normalizedProvider = MusicProviders.NormalizeOrDefault(jobDto.Provider);

    await _unitOfWork.BeginTransactionAsync();
    try
    {
      var user = await _unitOfWork.Users.GetByIdAsync(userId)
        ?? throw new KeyNotFoundException($"User {userId} not found");

      await EnsureConnectedAsync(user.Id, normalizedProvider);

      var sourcePlaylist = await ValidateAndGetPlaylistAsync(user.Id, normalizedProvider, jobDto.SourcePlaylistId);
      var job = CreateJob(userId, sourcePlaylist, jobDto.TargetPlaylistName, normalizedProvider);

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

  // Fail fast before any writes when the user has no usable connection for the provider —
  // otherwise the failure surfaces minutes later inside the background job.
  private async Task EnsureConnectedAsync(int userId, string provider)
  {
    if (!await _musicTokenService.HasValidTokensAsync(userId, provider))
    {
      throw new UnauthorizedAccessException(
        $"No valid {provider} connection for user {userId}; connect the account before creating a job.");
    }
  }

  private async Task<PlaylistSummary> ValidateAndGetPlaylistAsync(int userId, string provider, string playlistId)
  {
    var musicService = _musicServiceFactory.GetService(provider);
    var playlists = await musicService.GetUserPlaylistsAsync(userId, CancellationToken.None);
    var playlist = playlists.FirstOrDefault(p => p.Id == playlistId)
      ?? throw new KeyNotFoundException($"Playlist {playlistId} not found or user lacks access");

    return playlist;
  }

  private CleanPlaylistJob CreateJob(int userId, PlaylistSummary sourcePlaylist, string? targetName, string provider)
  {
    return new CleanPlaylistJob
    {
      UserId = userId,
      Provider = provider,
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
      Provider = job.Provider,
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
