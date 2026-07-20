using Hangfire;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Platform-neutral playlist cleaner that drives the track-by-track loop against any
/// <see cref="IMusicService"/> implementation. The factory supplies the provider-specific
/// music service, so one cleaner class serves every platform.
/// </summary>
public class PlaylistCleaner : IPlaylistCleaner
{
  private readonly IMusicService _musicService;
  private readonly IProgressTracker _progressTracker;
  private readonly IProgressBroadcastService _progressService;
  private readonly IUnitOfWork _unitOfWork;
  private readonly ILogger<PlaylistCleaner> _logger;
  private readonly BatchConfiguration _batchConfig;

  public PlaylistCleaner(
      IMusicService musicService,
      IProgressTracker progressTracker,
      IProgressBroadcastService progressService,
      IUnitOfWork unitOfWork,
      ILogger<PlaylistCleaner> logger,
      BatchConfiguration? batchConfig = null)
  {
    _musicService = musicService;
    _progressTracker = progressTracker;
    _progressService = progressService;
    _unitOfWork = unitOfWork;
    _logger = logger;
    _batchConfig = batchConfig ?? new BatchConfiguration();
  }

  public async Task<PlaylistCleaningResult> CleanPlaylistAsync(
      CleanPlaylistJob job,
      User user,
      IJobCancellationToken? cancellationToken = null)
  {
    var hangfireCancellationToken = cancellationToken ?? JobCancellationToken.Null;
    var shutdownToken = ResolveShutdownToken(hangfireCancellationToken);

    ThrowIfCancellationRequested(hangfireCancellationToken);
    var tracks = await _musicService.GetPlaylistTracksAsync(user.Id, job.SourcePlaylistId, shutdownToken);
    _progressTracker.Initialize(tracks.Count, _batchConfig);

    var processedResult = await ProcessTracks(job, user, tracks, hangfireCancellationToken, shutdownToken);
    var playlist = await CreateTargetPlaylist(
        user,
        job.TargetPlaylistName,
        processedResult.CleanTrackUris,
        hangfireCancellationToken,
        shutdownToken);

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
      IReadOnlyList<MusicTrack> tracks,
      IJobCancellationToken cancellationToken,
      CancellationToken shutdownToken)
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

      ThrowIfCancellationRequested(cancellationToken);
      var cleanVersion = await _musicService.FindCleanVersionAsync(user.Id, track, shutdownToken);

      var mapping = new TrackMapping
      {
        JobId = job.Id,
        SourceTrackId = track.Id,
        SourceTrackName = track.Name ?? "Unknown",
        SourceArtistName = track.Artists.Count > 0 ? string.Join(", ", track.Artists.Select(a => a.Name)) : "Unknown",
        IsExplicit = track.IsExplicit,
        HasCleanMatch = cleanVersion != null,
        TargetTrackId = cleanVersion?.Id,
        TargetTrackName = cleanVersion?.Name,
        TargetArtistName = cleanVersion?.Artists.Count > 0 ? string.Join(", ", cleanVersion.Artists.Select(a => a.Name)) : null,
        CreatedAt = DateTime.UtcNow
      };

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

    if (mappingBatch.Any())
    {
      await PersistMappings(mappingBatch);
    }

    return result;
  }

  private static bool IsValidTrack(MusicTrack track) => !string.IsNullOrEmpty(track.Id);

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

  private async Task<PlaylistSummary> CreateTargetPlaylist(
      User user,
      string playlistName,
      List<string> trackIds,
      IJobCancellationToken cancellationToken,
      CancellationToken shutdownToken)
  {
    ThrowIfCancellationRequested(cancellationToken);
    var playlist = await _musicService.CreatePlaylistAsync(
        user.Id,
        playlistName,
        "Cleaned by RadioWash.",
        shutdownToken);

    if (trackIds.Any())
    {
      ThrowIfCancellationRequested(cancellationToken);
      // Pass raw platform IDs; the adapter decides URI format.
      await _musicService.AddTracksToPlaylistAsync(user.Id, playlist.Id, trackIds, shutdownToken);
    }

    return playlist;
  }

  // JobCancellationToken.Null (the sentinel used at enqueue time and in direct unit tests) has
  // no backing ShutdownToken property, so accessing it throws NullReferenceException. Swallow
  // that here and fall back to a non-cancellable token; real Hangfire runtime tokens expose a
  // valid ShutdownToken for cooperative server-shutdown cancellation.
  private static CancellationToken ResolveShutdownToken(IJobCancellationToken token)
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

  private static void ThrowIfCancellationRequested(IJobCancellationToken token)
  {
    try
    {
      token.ThrowIfCancellationRequested();
    }
    catch (NullReferenceException)
    {
      // JobCancellationToken.Null also throws here; treat it as a non-cancellable sentinel for
      // direct unit tests and enqueue-time placeholders.
    }
  }
}
