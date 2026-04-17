using Microsoft.Extensions.DependencyInjection;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Platform-neutral playlist cleaner that drives the track-by-track loop against any
/// <see cref="IMusicService"/> implementation. The name retains "Spotify" for back-compat
/// with DI and the factory; it now operates through the provider-agnostic interface and no
/// longer knows about Spotify-specific types or URI formats.
/// </summary>
public class SpotifyPlaylistCleaner : IPlaylistCleaner
{
  private readonly IMusicService _musicService;
  private readonly IProgressTracker _progressTracker;
  private readonly IProgressBroadcastService _progressService;
  private readonly IUnitOfWork _unitOfWork;
  private readonly ILogger<SpotifyPlaylistCleaner> _logger;
  private readonly BatchConfiguration _batchConfig;

  public SpotifyPlaylistCleaner(
      [FromKeyedServices(SpotifyMusicService.Provider)] IMusicService musicService,
      IProgressTracker progressTracker,
      IProgressBroadcastService progressService,
      IUnitOfWork unitOfWork,
      ILogger<SpotifyPlaylistCleaner> logger,
      BatchConfiguration? batchConfig = null)
  {
    _musicService = musicService;
    _progressTracker = progressTracker;
    _progressService = progressService;
    _unitOfWork = unitOfWork;
    _logger = logger;
    _batchConfig = batchConfig ?? new BatchConfiguration();
  }

  public async Task<PlaylistCleaningResult> CleanPlaylistAsync(CleanPlaylistJob job, User user)
  {
    var tracks = await _musicService.GetPlaylistTracksAsync(user.Id, job.SourcePlaylistId, CancellationToken.None);
    _progressTracker.Initialize(tracks.Count, _batchConfig);

    var processedResult = await ProcessTracks(job, user, tracks);
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
      IReadOnlyList<MusicTrack> tracks)
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

      var cleanVersion = await _musicService.FindCleanVersionAsync(user.Id, track, CancellationToken.None);

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
      List<string> trackIds)
  {
    var playlist = await _musicService.CreatePlaylistAsync(
        user.Id,
        playlistName,
        "Cleaned by RadioWash.",
        CancellationToken.None);

    if (trackIds.Any())
    {
      // Pass raw platform IDs; the adapter decides URI format.
      await _musicService.AddTracksToPlaylistAsync(user.Id, playlist.Id, trackIds, CancellationToken.None);
    }

    return playlist;
  }
}
