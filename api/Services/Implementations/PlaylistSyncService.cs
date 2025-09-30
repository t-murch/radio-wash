using System.Diagnostics;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Infrastructure.Logging;
using RadioWash.Api.Infrastructure.Monitoring;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class PlaylistSyncService : IPlaylistSyncService
{
  private readonly IUnitOfWork _unitOfWork;
  private readonly ISpotifyService _spotifyService;
  private readonly IPlaylistDeltaCalculator _deltaCalculator;
  private readonly ITrackProcessor _trackProcessor;
  private readonly ISubscriptionService _subscriptionService;
  private readonly ISyncTimeCalculator _timeCalculator;
  private readonly SyncMetrics _syncMetrics;
  private readonly ISyncPerformanceMonitor _performanceMonitor;
  private readonly ILogger<PlaylistSyncService> _logger;

  public PlaylistSyncService(
      IUnitOfWork unitOfWork,
      ISpotifyService spotifyService,
      IPlaylistDeltaCalculator deltaCalculator,
      ITrackProcessor trackProcessor,
      ISubscriptionService subscriptionService,
      ISyncTimeCalculator timeCalculator,
      SyncMetrics syncMetrics,
      ISyncPerformanceMonitor performanceMonitor,
      ILogger<PlaylistSyncService> logger)
  {
    _unitOfWork = unitOfWork;
    _spotifyService = spotifyService;
    _deltaCalculator = deltaCalculator;
    _trackProcessor = trackProcessor;
    _subscriptionService = subscriptionService;
    _timeCalculator = timeCalculator;
    _syncMetrics = syncMetrics;
    _performanceMonitor = performanceMonitor;
    _logger = logger;
  }

  public async Task<PlaylistSyncResult> SyncPlaylistAsync(PlaylistSyncConfig config)
  {
    var stopwatch = Stopwatch.StartNew();
    
    // Start metrics and performance tracking
    _syncMetrics.RecordSyncStarted(config.Id, config.UserId, config.SyncFrequency);
    using var performanceScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "full_sync");
    using var loggingScope = _logger.BeginSyncScope(config.Id, config.UserId, "sync_playlist");

    // Create sync history entry
    var syncHistory = new PlaylistSyncHistory
    {
      SyncConfigId = config.Id,
      StartedAt = DateTime.UtcNow,
      Status = SyncStatus.Running
    };

    using var dbScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "create_sync_history");
    await _unitOfWork.SyncHistory.CreateAsync(syncHistory);
    
    try
    {
      _logger.LogSyncStarted(config.Id, config.UserId);

      // Verify user has active subscription
      _logger.LogSubscriptionValidationStarted(config.UserId);
      var hasActiveSubscription = await _subscriptionService.HasActiveSubscriptionAsync(config.UserId);
      _logger.LogSubscriptionValidationCompleted(config.UserId, hasActiveSubscription);
      _syncMetrics.RecordSubscriptionValidation(config.UserId, hasActiveSubscription);
      
      if (!hasActiveSubscription)
      {
        _logger.LogWarning("User {UserId} does not have active subscription, disabling sync config {ConfigId}",
            config.UserId, config.Id);
        await _unitOfWork.SyncConfigs.DisableConfigAsync(config.Id);

        throw new InvalidOperationException("User does not have an active subscription");
      }

      // 1. Fetch current source playlist
      IEnumerable<Models.Spotify.SpotifyTrack> sourcePlaylist;
      using (var sourceScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "fetch_source_playlist"))
      {
        sourcePlaylist = await _spotifyService.GetPlaylistTracksAsync(
            config.UserId,
            config.SourcePlaylistId
        );
      }

      // 2. Fetch current target playlist
      List<Models.Spotify.SpotifyTrack> targetPlaylist;
      using (var targetScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "fetch_target_playlist"))
      {
        var targetPlaylistTracks = await _spotifyService.GetPlaylistTracksAsync(
            config.UserId,
            config.TargetPlaylistId
        );
        targetPlaylist = targetPlaylistTracks.ToList();
      }

      // 3. Get existing track mappings from original job
      IEnumerable<TrackMapping> existingMappings;
      using (var mappingScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "fetch_track_mappings"))
      {
        existingMappings = await _unitOfWork.TrackMappings.GetByJobIdAsync(config.OriginalJobId);
        _performanceMonitor.RecordDatabaseOperation("track_mappings_fetch", 0, existingMappings.Count());
      }

      // 4. Calculate delta (what needs to be added/removed)
      PlaylistDelta delta;
      using (var deltaScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "calculate_delta"))
      {
        delta = await _deltaCalculator.CalculateDeltaAsync(
            sourcePlaylist.ToList(),
            targetPlaylist,
            existingMappings.ToList()
        );
        _logger.LogInformation("Delta calculated: {TracksToAdd} to add, {TracksToRemove} to remove, {NewTracks} new tracks",
            delta.TracksToAdd.Count, delta.TracksToRemove.Count, delta.NewTracks.Count);
      }

      // 5. Process new tracks (find clean versions)
      var newMappings = await ProcessNewTracksAsync(delta.NewTracks, config);

      // 6. Apply changes to target playlist
      await ApplyDeltaToPlaylistAsync(config, delta, newMappings);

      stopwatch.Stop();

      var tracksAdded = delta.TracksToAdd.Count + newMappings.Count(m => m.HasCleanMatch);
      var tracksRemoved = delta.TracksToRemove.Count;

      // 7. Update sync history and config
      using (var updateScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "update_sync_history"))
      {
        await _unitOfWork.SyncHistory.CompleteHistoryAsync(
            syncHistory.Id,
            tracksAdded,
            tracksRemoved,
            targetPlaylist.Count - tracksRemoved,
            (int)stopwatch.ElapsedMilliseconds
        );

        await _unitOfWork.SyncConfigs.UpdateLastSyncAsync(
            config.Id,
            DateTime.UtcNow,
            SyncStatus.Completed
        );

        // Schedule next sync
        var nextSync = _timeCalculator.CalculateNextSyncTime(config.SyncFrequency, DateTime.UtcNow);
        await _unitOfWork.SyncConfigs.UpdateNextScheduledSyncAsync(config.Id, nextSync);
      }

      // Record metrics and log completion
      _syncMetrics.RecordSyncCompleted(config.Id, config.UserId, stopwatch.ElapsedMilliseconds, 
          tracksAdded, tracksRemoved, config.SyncFrequency);
      _logger.LogSyncCompleted(config.Id, config.UserId, tracksAdded, tracksRemoved, stopwatch.ElapsedMilliseconds);

      return new PlaylistSyncResult
      {
        Success = true,
        TracksAdded = delta.TracksToAdd.Count + newMappings.Count(m => m.HasCleanMatch),
        TracksRemoved = delta.TracksToRemove.Count,
        TracksUnchanged = targetPlaylist.Count - delta.TracksToRemove.Count,
        ExecutionTime = stopwatch.Elapsed
      };
    }
    catch (Exception ex)
    {
      stopwatch.Stop();
      var errorType = SyncLoggingExtensions.GetErrorType(ex);
      
      // Record metrics and log failure
      _syncMetrics.RecordSyncFailed(config.Id, config.UserId, stopwatch.ElapsedMilliseconds, errorType, config.SyncFrequency);
      _logger.LogSyncFailed(config.Id, config.UserId, stopwatch.ElapsedMilliseconds, ex.Message, ex);

      await _unitOfWork.SyncHistory.FailHistoryAsync(syncHistory.Id, ex.Message);
      await _unitOfWork.SyncConfigs.UpdateLastSyncAsync(config.Id, DateTime.UtcNow, SyncStatus.Failed, ex.Message);

      return new PlaylistSyncResult
      {
        Success = false,
        ErrorMessage = ex.Message,
        ExecutionTime = stopwatch.Elapsed
      };
    }
  }

  public async Task<PlaylistSyncConfig?> EnableSyncForJobAsync(int jobId, int userId)
  {
    // Check if user has active subscription
    var hasActiveSubscription = await _subscriptionService.HasActiveSubscriptionAsync(userId);
    if (!hasActiveSubscription)
    {
      throw new InvalidOperationException("Active subscription required to enable sync");
    }

    // Check if sync config already exists
    var existingConfig = await _unitOfWork.SyncConfigs.GetByJobIdAsync(jobId);
    if (existingConfig != null)
    {
      if (!existingConfig.IsActive)
      {
        existingConfig.IsActive = true;
        existingConfig.UpdatedAt = DateTime.UtcNow;
        var nextSync = _timeCalculator.CalculateNextSyncTime(existingConfig.SyncFrequency);
        existingConfig.NextScheduledSync = nextSync;
        return await _unitOfWork.SyncConfigs.UpdateAsync(existingConfig);
      }
      return existingConfig;
    }

    // Get the original job
    var job = await _unitOfWork.Jobs.GetByIdAsync(jobId);
    if (job == null || job.UserId != userId)
    {
      throw new InvalidOperationException("Job not found or access denied");
    }

    if (job.Status != JobStatus.Completed)
    {
      throw new InvalidOperationException("Can only enable sync for completed jobs");
    }

    // Create new sync config
    var config = new PlaylistSyncConfig
    {
      UserId = userId,
      OriginalJobId = jobId,
      SourcePlaylistId = job.SourcePlaylistId,
      TargetPlaylistId = job.TargetPlaylistId!,
      IsActive = true,
      SyncFrequency = SyncFrequency.Daily,
      NextScheduledSync = _timeCalculator.CalculateNextSyncTime(SyncFrequency.Daily),
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    _logger.LogSyncConfigEnabled(jobId, userId, config.SyncFrequency);
    return await _unitOfWork.SyncConfigs.CreateAsync(config);
  }

  public async Task<bool> DisableSyncAsync(int syncConfigId, int userId)
  {
    var config = await _unitOfWork.SyncConfigs.GetByIdAsync(syncConfigId);
    if (config == null || config.UserId != userId)
    {
      return false;
    }

    _logger.LogSyncConfigDisabled(syncConfigId, userId);
    await _unitOfWork.SyncConfigs.DisableConfigAsync(syncConfigId);
    return true;
  }

  public async Task<IEnumerable<PlaylistSyncConfig>> GetUserSyncConfigsAsync(int userId)
  {
    return await _unitOfWork.SyncConfigs.GetByUserIdAsync(userId);
  }

  public async Task<PlaylistSyncConfig?> UpdateSyncFrequencyAsync(int syncConfigId, string frequency, int userId)
  {
    var config = await _unitOfWork.SyncConfigs.GetByIdAsync(syncConfigId);
    if (config == null || config.UserId != userId)
    {
      return null;
    }

    config.SyncFrequency = frequency;
    config.NextScheduledSync = _timeCalculator.CalculateNextSyncTime(frequency, config.LastSyncedAt);
    config.UpdatedAt = DateTime.UtcNow;

    return await _unitOfWork.SyncConfigs.UpdateAsync(config);
  }

  public async Task<PlaylistSyncResult> ManualSyncAsync(int syncConfigId, int userId)
  {
    var config = await _unitOfWork.SyncConfigs.GetByIdAsync(syncConfigId);
    if (config == null || config.UserId != userId)
    {
      throw new InvalidOperationException("Sync configuration not found or access denied");
    }

    if (!config.IsActive)
    {
      throw new InvalidOperationException("Sync configuration is disabled");
    }

    _logger.LogManualSyncRequested(syncConfigId, userId, "user_request");
    return await SyncPlaylistAsync(config);
  }

  public async Task<IEnumerable<PlaylistSyncHistory>> GetSyncHistoryAsync(int syncConfigId, int limit = 20)
  {
    return await _unitOfWork.SyncHistory.GetByConfigIdAsync(syncConfigId, limit);
  }

  private async Task<List<TrackMapping>> ProcessNewTracksAsync(List<Models.Spotify.SpotifyTrack> newTracks, PlaylistSyncConfig config)
  {
    var newMappings = new List<TrackMapping>();

    if (!newTracks.Any())
    {
      return newMappings;
    }

    _logger.LogNewTracksProcessing(newTracks.Count, config.Id, config.UserId);

    foreach (var track in newTracks)
    {
      try
      {
        using var trackScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "process_track");
        var cleanTrack = await _trackProcessor.FindCleanVersionAsync(track);
        _performanceMonitor.RecordTrackProcessingTime(0, cleanTrack != null);
        
        var mapping = new TrackMapping
        {
          JobId = config.OriginalJobId,
          SourceTrackId = track.Id,
          SourceTrackName = track.Name,
          SourceArtistName = track.Artists.FirstOrDefault()?.Name ?? "Unknown",
          HasCleanMatch = cleanTrack != null,
          TargetTrackId = cleanTrack?.Id,
          TargetTrackName = cleanTrack?.Name,
          TargetArtistName = cleanTrack?.Artists.FirstOrDefault()?.Name,
          CreatedAt = DateTime.UtcNow
        };

        newMappings.Add(mapping);
        await _unitOfWork.TrackMappings.AddAsync(mapping);
      }
      catch (Exception ex)
      {
        _logger.LogTrackProcessingFailed(track.Id, track.Name, ex);
      }
    }

    await _unitOfWork.SaveChangesAsync();
    _syncMetrics.RecordTracksProcessed(newTracks.Count, config.UserId);
    return newMappings;
  }

  private async Task ApplyDeltaToPlaylistAsync(PlaylistSyncConfig config, PlaylistDelta delta, List<TrackMapping> newMappings)
  {
    // Add clean versions of new tracks
    var tracksToAdd = delta.TracksToAdd.ToList();
    tracksToAdd.AddRange(newMappings.Where(m => m.HasCleanMatch && !string.IsNullOrEmpty(m.TargetTrackId)).Select(m => m.TargetTrackId!));

    if (tracksToAdd.Any())
    {
      _logger.LogPlaylistOperation("add_tracks", tracksToAdd.Count, config.TargetPlaylistId);
      using var addScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "add_tracks_to_playlist");
      await _spotifyService.AddTracksToPlaylistAsync(config.UserId, config.TargetPlaylistId, tracksToAdd);
    }

    // Remove tracks that are no longer in source
    if (delta.TracksToRemove.Any())
    {
      _logger.LogPlaylistOperation("remove_tracks", delta.TracksToRemove.Count, config.TargetPlaylistId);
      using var removeScope = _performanceMonitor.BeginSyncOperation(config.Id, config.UserId, "remove_tracks_from_playlist");
      await _spotifyService.RemoveTracksFromPlaylistAsync(config.UserId, config.TargetPlaylistId, delta.TracksToRemove);
    }
  }
}
