using System.Diagnostics;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class PlaylistSyncService : IPlaylistSyncService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISpotifyService _spotifyService;
    private readonly IPlaylistDeltaCalculator _deltaCalculator;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ISyncTimeCalculator _syncTimeCalculator;
    private readonly ILogger<PlaylistSyncService> _logger;

    public PlaylistSyncService(
        IUnitOfWork unitOfWork,
        ISpotifyService spotifyService,
        IPlaylistDeltaCalculator deltaCalculator,
        ISubscriptionService subscriptionService,
        ISyncTimeCalculator syncTimeCalculator,
        ILogger<PlaylistSyncService> logger)
    {
        _unitOfWork = unitOfWork;
        _spotifyService = spotifyService;
        _deltaCalculator = deltaCalculator;
        _subscriptionService = subscriptionService;
        _syncTimeCalculator = syncTimeCalculator;
        _logger = logger;
    }

    public async Task<PlaylistSyncResult> SyncPlaylistAsync(int configId)
    {
        var stopwatch = Stopwatch.StartNew();

        // Load the sync config from database
        var config = await _unitOfWork.SyncConfigs.GetByIdAsync(configId);
        if (config == null)
        {
            throw new InvalidOperationException($"Sync config with ID {configId} not found");
        }

        // Create sync history entry
        var syncHistory = new PlaylistSyncHistory
        {
            SyncConfigId = config.Id,
            StartedAt = DateTime.UtcNow,
            Status = SyncStatus.Running
        };

        await _unitOfWork.SyncHistory.CreateAsync(syncHistory);

        try
        {
            _logger.LogInformation("Starting sync for config {ConfigId}, user {UserId}", config.Id, config.UserId);

            // Verify user has active subscription
            var hasActiveSubscription = await _subscriptionService.HasActiveSubscriptionAsync(config.UserId);
            if (!hasActiveSubscription)
            {
                _logger.LogWarning("User {UserId} does not have active subscription, disabling sync config {ConfigId}",
                    config.UserId, config.Id);
                await _unitOfWork.SyncConfigs.DisableConfigAsync(config.Id);

                throw new InvalidOperationException("User does not have an active subscription");
            }

            // 1. Fetch current source playlist
            var sourcePlaylist = await _spotifyService.GetPlaylistTracksAsync(
                config.UserId,
                config.SourcePlaylistId
            );

            // 2. Fetch current target playlist
            var targetPlaylistTracks = await _spotifyService.GetPlaylistTracksAsync(
                config.UserId,
                config.TargetPlaylistId
            );
            var targetPlaylist = targetPlaylistTracks.ToList();

            // 3. Get existing track mappings from original job
            var existingMappings = await _unitOfWork.TrackMappings.GetByJobIdAsync(config.OriginalJobId);

            // 4. Calculate delta (what needs to be added/removed)
            var delta = await _deltaCalculator.CalculateDeltaAsync(
                sourcePlaylist.ToList(),
                targetPlaylist,
                existingMappings.ToList()
            );

            // 5. Process new tracks (find clean versions)
            var newMappings = await ProcessNewTracksAsync(delta.NewTracks, config);

            // 6. Apply changes to target playlist
            await ApplyDeltaToPlaylistAsync(config, delta, newMappings);

            stopwatch.Stop();

            // 7. Update sync history and config
            await _unitOfWork.SyncHistory.CompleteHistoryAsync(
                syncHistory.Id,
                delta.TracksToAdd.Count + newMappings.Count(m => m.HasCleanMatch),
                delta.TracksToRemove.Count,
                targetPlaylist.Count - delta.TracksToRemove.Count,
                (int)stopwatch.ElapsedMilliseconds
            );

            await _unitOfWork.SyncConfigs.UpdateLastSyncAsync(
                config.Id,
                DateTime.UtcNow,
                SyncStatus.Completed
            );

            // Schedule next sync
            var nextSync = _syncTimeCalculator.CalculateNextSyncTime(config.SyncFrequency, DateTime.UtcNow);
            await _unitOfWork.SyncConfigs.UpdateNextScheduledSyncAsync(config.Id, nextSync);

            _logger.LogInformation("Sync completed for config {ConfigId}. Added: {Added}, Removed: {Removed}, Time: {ElapsedMs}ms",
                config.Id, delta.TracksToAdd.Count + newMappings.Count(m => m.HasCleanMatch), delta.TracksToRemove.Count, stopwatch.ElapsedMilliseconds);

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
            _logger.LogError(ex, "Sync failed for config {ConfigId}", config.Id);

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
                var nextSync = _syncTimeCalculator.CalculateNextSyncTime(existingConfig.SyncFrequency);
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
            NextScheduledSync = _syncTimeCalculator.CalculateNextSyncTime(SyncFrequency.Daily),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Enabling sync for job {JobId}, user {UserId}", jobId, userId);
        return await _unitOfWork.SyncConfigs.CreateAsync(config);
    }

    public async Task<bool> DisableSyncAsync(int syncConfigId, int userId)
    {
        var config = await _unitOfWork.SyncConfigs.GetByIdAsync(syncConfigId);
        if (config == null || config.UserId != userId)
        {
            return false;
        }

        _logger.LogInformation("Disabling sync for config {ConfigId}, user {UserId}", syncConfigId, userId);
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
        config.NextScheduledSync = _syncTimeCalculator.CalculateNextSyncTime(frequency, config.LastSyncedAt);
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

        _logger.LogInformation("Manual sync requested for config {ConfigId}, user {UserId}", syncConfigId, userId);
        return await SyncPlaylistAsync(config.Id);
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

        _logger.LogInformation("Processing {NewTrackCount} new tracks for config {ConfigId}", newTracks.Count, config.Id);

        foreach (var track in newTracks)
        {
            try
            {
                var cleanTrack = await _spotifyService.FindCleanVersionAsync(config.UserId, track);
                var mapping = new TrackMapping
                {
                    JobId = config.OriginalJobId,
                    SourceTrackId = track.Id,
                    SourceTrackName = track.Name,
                    SourceArtistName = string.Join(", ", track.Artists.Select(a => a.Name)),
                    IsExplicit = track.Explicit,
                    HasCleanMatch = cleanTrack != null,
                    TargetTrackId = cleanTrack?.Id,
                    TargetTrackName = cleanTrack?.Name,
                    TargetArtistName = cleanTrack != null ? string.Join(", ", cleanTrack.Artists.Select(a => a.Name)) : null,
                    CreatedAt = DateTime.UtcNow
                };

                newMappings.Add(mapping);
                await _unitOfWork.TrackMappings.AddAsync(mapping);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process new track {TrackId} ({TrackName})", track.Id, track.Name);
            }
        }

        await _unitOfWork.SaveChangesAsync();
        return newMappings;
    }

    private async Task ApplyDeltaToPlaylistAsync(PlaylistSyncConfig config, PlaylistDelta delta, List<TrackMapping> newMappings)
    {
        // Add clean versions of new tracks
        var trackIdsToAdd = delta.TracksToAdd.ToList();
        trackIdsToAdd.AddRange(newMappings.Where(m => m.HasCleanMatch && !string.IsNullOrEmpty(m.TargetTrackId)).Select(m => m.TargetTrackId!));

        if (trackIdsToAdd.Any())
        {
            // Convert track IDs to Spotify URIs
            var trackUris = trackIdsToAdd.Select(id => $"spotify:track:{id}").ToList();
            _logger.LogInformation("Adding {TrackCount} tracks to playlist {PlaylistId}", trackUris.Count, config.TargetPlaylistId);
            await _spotifyService.AddTracksToPlaylistAsync(config.UserId, config.TargetPlaylistId, trackUris);
        }

        // Remove tracks that are no longer in source
        if (delta.TracksToRemove.Any())
        {
            // Convert track IDs to Spotify URIs for removal
            var trackUrisToRemove = delta.TracksToRemove.Select(id => $"spotify:track:{id}").ToList();
            _logger.LogInformation("Removing {TrackCount} tracks from playlist {PlaylistId}", trackUrisToRemove.Count, config.TargetPlaylistId);
            await _spotifyService.RemoveTracksFromPlaylistAsync(config.UserId, config.TargetPlaylistId, trackUrisToRemove);
        }
    }
}
