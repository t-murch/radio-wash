using Hangfire;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Cross-service copy engine. Structure mirrors <see cref="PlaylistCleaner"/> — the same
/// progress tracking, TrackMapping batch persistence, and Hangfire cancellation semantics —
/// but reads via the source provider's IMusicService and writes via the target's, bridging
/// catalogs through <see cref="ITrackMatcher"/>. Kept separate from the cleaner so the
/// existing same-service clean path stays untouched.
/// </summary>
public class PlaylistCopier : IPlaylistCopier
{
  // Upper bound on ISRCs sent to the batched prefetch. Apple resolves these 25-per-request,
  // but a provider without a batch ISRC endpoint spends one search per ISRC, so an uncapped
  // prefetch on a large playlist front-loads hundreds of sequential calls before the first
  // track is matched. Past the cap, tracks simply fall through to the search fallback in
  // TrackMatcher — a lower-confidence match, not a dropped track.
  private const int MaxPrefetchIsrcs = 200;

  private readonly IMusicServiceFactory _musicServiceFactory;
  private readonly ITrackMatcher _trackMatcher;
  private readonly IProgressTracker _progressTracker;
  private readonly IProgressBroadcastService _progressService;
  private readonly IUnitOfWork _unitOfWork;
  private readonly ILogger<PlaylistCopier> _logger;
  private readonly BatchConfiguration _batchConfig;

  public PlaylistCopier(
      IMusicServiceFactory musicServiceFactory,
      ITrackMatcher trackMatcher,
      IProgressTracker progressTracker,
      IProgressBroadcastService progressService,
      IUnitOfWork unitOfWork,
      ILogger<PlaylistCopier> logger,
      BatchConfiguration? batchConfig = null)
  {
    _musicServiceFactory = musicServiceFactory;
    _trackMatcher = trackMatcher;
    _progressTracker = progressTracker;
    _progressService = progressService;
    _unitOfWork = unitOfWork;
    _logger = logger;
    _batchConfig = batchConfig ?? new BatchConfiguration();
  }

  public async Task<PlaylistCleaningResult> CopyPlaylistAsync(
      CleanPlaylistJob job,
      User user,
      IJobCancellationToken? cancellationToken = null)
  {
    var hangfireCancellationToken = cancellationToken ?? JobCancellationToken.Null;
    var shutdownToken = HangfireCancellationHelper.ResolveShutdownToken(hangfireCancellationToken);

    var source = _musicServiceFactory.GetService(job.Provider);
    var target = _musicServiceFactory.GetService(job.TargetProvider);

    HangfireCancellationHelper.ThrowIfCancellationRequested(hangfireCancellationToken);
    var tracks = await source.GetPlaylistTracksAsync(user.Id, job.SourcePlaylistId, shutdownToken);
    _progressTracker.Initialize(tracks.Count, _batchConfig);

    // One batched ISRC resolution up front; per-track matching then hits the index instead
    // of the network for every ISRC-carrying track.
    var distinctIsrcs = tracks
      .Select(t => t.Isrc)
      .Where(isrc => !string.IsNullOrEmpty(isrc))
      .Select(isrc => isrc!)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    var isrcs = distinctIsrcs.Take(MaxPrefetchIsrcs).ToList();
    if (distinctIsrcs.Count > isrcs.Count)
    {
      _logger.LogInformation(
        "Job {JobId}: {Total} distinct ISRCs exceeds the {Cap} prefetch cap; the remaining " +
        "{Overflow} tracks resolve through the search fallback instead of ISRC lookup",
        job.Id, distinctIsrcs.Count, MaxPrefetchIsrcs, distinctIsrcs.Count - isrcs.Count);
    }

    var isrcIndex = await _trackMatcher.PrefetchByIsrcAsync(user.Id, target, isrcs, shutdownToken);

    var processedResult = await ProcessTracks(job, user, target, tracks, isrcIndex, hangfireCancellationToken, shutdownToken);
    var playlist = await CreateTargetPlaylist(
        user,
        target,
        job,
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
      IMusicService target,
      IReadOnlyList<MusicTrack> tracks,
      IReadOnlyDictionary<string, MusicTrack> isrcIndex,
      IJobCancellationToken cancellationToken,
      CancellationToken shutdownToken)
  {
    var result = new TrackProcessingResult();
    var mappingBatch = new List<TrackMapping>();

    for (int i = 0; i < tracks.Count; i++)
    {
      var track = tracks[i];

      if (string.IsNullOrEmpty(track.Id))
      {
        _logger.LogWarning("Skipping invalid track: {TrackName}", track.Name ?? "Unknown");
        continue;
      }

      HangfireCancellationHelper.ThrowIfCancellationRequested(cancellationToken);
      var match = await _trackMatcher.MatchAsync(
          user.Id, target, track, isrcIndex, job.SwapExplicitForClean, shutdownToken);

      var mapping = new TrackMapping
      {
        JobId = job.Id,
        SourceTrackId = track.Id,
        SourceTrackName = track.Name ?? "Unknown",
        SourceArtistName = track.Artists.Count > 0 ? string.Join(", ", track.Artists.Select(a => a.Name)) : "Unknown",
        IsExplicit = track.IsExplicit,
        HasCleanMatch = match.Target != null,
        TargetTrackId = match.Target?.Id,
        TargetTrackName = match.Target?.Name,
        TargetArtistName = match.Target?.Artists.Count > 0 ? string.Join(", ", match.Target.Artists.Select(a => a.Name)) : null,
        Isrc = track.Isrc,
        MatchMethod = match.Method,
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
      IMusicService target,
      CleanPlaylistJob job,
      List<string> trackIds,
      IJobCancellationToken cancellationToken,
      CancellationToken shutdownToken)
  {
    HangfireCancellationHelper.ThrowIfCancellationRequested(cancellationToken);
    var playlist = await target.CreatePlaylistAsync(
        user.Id,
        job.TargetPlaylistName,
        $"Copied from {job.SourcePlaylistName} by RadioWash.",
        shutdownToken);

    if (trackIds.Any())
    {
      HangfireCancellationHelper.ThrowIfCancellationRequested(cancellationToken);
      // Pass raw platform IDs; the target adapter decides URI format.
      await target.AddTracksToPlaylistAsync(user.Id, playlist.Id, trackIds, shutdownToken);
    }

    return playlist;
  }
}
