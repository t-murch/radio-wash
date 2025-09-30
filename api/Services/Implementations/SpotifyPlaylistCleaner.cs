using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Spotify-specific implementation of playlist cleaner
/// </summary>
public class SpotifyPlaylistCleaner : IPlaylistCleaner
{
  private readonly ISpotifyService _spotifyService;
  private readonly ITrackProcessor _trackProcessor;
  private readonly IProgressTracker _progressTracker;
  private readonly IProgressBroadcastService _progressService;
  private readonly IUnitOfWork _unitOfWork;
  private readonly ILogger<SpotifyPlaylistCleaner> _logger;
  private readonly BatchConfiguration _batchConfig;

  public SpotifyPlaylistCleaner(
      ISpotifyService spotifyService,
      ITrackProcessor trackProcessor,
      IProgressTracker progressTracker,
      IProgressBroadcastService progressService,
      IUnitOfWork unitOfWork,
      ILogger<SpotifyPlaylistCleaner> logger,
      BatchConfiguration? batchConfig = null)
  {
    _spotifyService = spotifyService;
    _trackProcessor = trackProcessor;
    _progressTracker = progressTracker;
    _progressService = progressService;
    _unitOfWork = unitOfWork;
    _logger = logger;
    _batchConfig = batchConfig ?? new BatchConfiguration();
  }

  public async Task<PlaylistCleaningResult> CleanPlaylistAsync(CleanPlaylistJob job, User user)
  {
    var tracks = await _spotifyService.GetPlaylistTracksAsync(user.Id, job.SourcePlaylistId);
    var trackList = tracks.ToList();

    _progressTracker.Initialize(trackList.Count, _batchConfig);

    var processedResult = await ProcessTracks(job, user, trackList);
    var playlist = await CreateTargetPlaylist(user, job.TargetPlaylistName, processedResult.CleanTrackUris);

    return new PlaylistCleaningResult
    {
      ProcessedTracks = processedResult.ProcessedCount,
      MatchedTracks = processedResult.MatchedCount,
      TargetPlaylistId = playlist.Id,
      CleanTrackUris = processedResult.CleanTrackUris
    };
  }

  private async Task<TrackProcessingResult> ProcessTracks(
      CleanPlaylistJob job,
      User user,
      List<SpotifyTrack> tracks)
  {
    var result = new TrackProcessingResult();
    var mappingBatch = new List<TrackMapping>();

    for (int i = 0; i < tracks.Count; i++)
    {
      var track = tracks[i];

      if (!IsValidTrack(track))
      {
        _logger.LogWarning("Skipping invalid track: {TrackName}", track.Name ?? "Unknown");
        continue;
      }

      var mapping = await _trackProcessor.ProcessTrackAsync(user.Id, job.Id, track);
      mappingBatch.Add(mapping);

      if (mapping.HasCleanMatch)
      {
        result.MatchedCount++;
        result.CleanTrackUris.Add(mapping.TargetTrackId!);
      }

      result.ProcessedCount++;

      await HandleProgressReporting(job.Id, i + 1, track.Name);
      await HandleBatchPersistence(job.Id, i + 1, mappingBatch);
    }

    // Save any remaining mappings
    if (mappingBatch.Any())
    {
      await PersistMappings(mappingBatch);
    }

    return result;
  }

  private bool IsValidTrack(SpotifyTrack track)
  {
    return !string.IsNullOrEmpty(track.Id);
  }

  private async Task HandleProgressReporting(int jobId, int processedCount, string? trackName)
  {
    if (_progressTracker.ShouldReportProgress(processedCount))
    {
      var update = _progressTracker.CreateUpdate(processedCount, trackName);
      try
      {
        await _progressService.BroadcastProgressUpdate(jobId, update);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to broadcast progress for job {JobId}", jobId);
      }
    }
  }

  private async Task HandleBatchPersistence(int jobId, int processedCount, List<TrackMapping> batch)
  {
    if (_progressTracker.ShouldPersistProgress(processedCount) && batch.Any())
    {
      await _unitOfWork.BeginTransactionAsync();
      try
      {
        await PersistMappings(batch);
        await _unitOfWork.Jobs.UpdateProgressAsync(
            jobId,
            processedCount,
            _progressTracker.CreateUpdate(processedCount).CurrentBatch);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();
        batch.Clear();
      }
      catch (Exception ex)
      {
        await _unitOfWork.RollbackTransactionAsync();
        _logger.LogError(ex, "Failed to persist batch for job {JobId}", jobId);
        throw;
      }
    }
  }

  private async Task PersistMappings(List<TrackMapping> mappings)
  {
    if (mappings.Any())
    {
      await _unitOfWork.TrackMappings.AddRangeAsync(mappings);
    }
  }

  private async Task<SpotifyPlaylist> CreateTargetPlaylist(
      User user,
      string playlistName,
      List<string> trackUris)
  {
    var playlist = await _spotifyService.CreatePlaylistAsync(
        user.Id,
        playlistName,
        "Cleaned by RadioWash.");

    if (trackUris.Any())
    {
      var uris = trackUris.Select(id => $"spotify:track:{id}");
      await _spotifyService.AddTracksToPlaylistAsync(user.Id, playlist.Id, uris);
    }

    return playlist;
  }
}
