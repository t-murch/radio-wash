using System.Diagnostics;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class PlaylistSyncService : IPlaylistSyncService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMusicServiceFactory _musicServiceFactory;
    private readonly IPlaylistDeltaCalculator _deltaCalculator;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ISyncTimeCalculator _syncTimeCalculator;
    private readonly ILogger<PlaylistSyncService> _logger;

    public PlaylistSyncService(
        IUnitOfWork unitOfWork,
        IMusicServiceFactory musicServiceFactory,
        IPlaylistDeltaCalculator deltaCalculator,
        ISubscriptionService subscriptionService,
        ISyncTimeCalculator syncTimeCalculator,
        ILogger<PlaylistSyncService> logger)
    {
        _unitOfWork = unitOfWork;
        _musicServiceFactory = musicServiceFactory;
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
                await _unitOfWork.SyncConfigs.DisableConfigAsync(config.Id, AutoDisableReason.SubscriptionInactive);

                throw new InvalidOperationException("User does not have an active subscription");
            }

            // A sync config carries no provider of its own — it inherits the provider of
            // the job that produced the clean playlist.
            var musicService = await ResolveMusicServiceAsync(config);

            // 1. Fetch current source playlist
            var sourcePlaylist = await musicService.GetPlaylistTracksAsync(
                config.UserId,
                config.SourcePlaylistId,
                CancellationToken.None
            );

            // 2. Fetch current target playlist
            var targetPlaylistTracks = await musicService.GetPlaylistTracksAsync(
                config.UserId,
                config.TargetPlaylistId,
                CancellationToken.None
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
            var newMappings = await ProcessNewTracksAsync(musicService, delta.NewTracks, config);

            // 6. Apply changes to target playlist
            await ApplyDeltaToPlaylistAsync(musicService, config, delta, newMappings);

            stopwatch.Stop();

            var tracksAdded = delta.TracksToAdd.Count + newMappings.Count(m => m.HasCleanMatch);

            // Sync is additive: nothing is ever removed (see ApplyDeltaToPlaylistAsync), so
            // the removed count is always zero and every pre-existing target track is
            // unchanged. Reporting delta.TracksToRemove here would claim removals that
            // never happened.
            if (delta.TracksToRemove.Count > 0)
            {
                _logger.LogInformation(
                    "Sync config {ConfigId}: {DriftCount} track(s) in the clean playlist no longer have a source track. " +
                    "They are left in place — the provider API cannot remove tracks from a library playlist.",
                    config.Id, delta.TracksToRemove.Count);
            }

            // 7. Update sync history and config
            await _unitOfWork.SyncHistory.CompleteHistoryAsync(
                syncHistory.Id,
                tracksAdded,
                0,
                targetPlaylist.Count,
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

            _logger.LogInformation("Sync completed for config {ConfigId}. Added: {Added}, Time: {ElapsedMs}ms",
                config.Id, tracksAdded, stopwatch.ElapsedMilliseconds);

            return new PlaylistSyncResult
            {
                Success = true,
                TracksAdded = tracksAdded,
                TracksRemoved = 0,
                TracksUnchanged = targetPlaylist.Count,
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

    /// <summary>
    /// A <see cref="PlaylistSyncConfig"/> has no provider column of its own; it inherits the
    /// provider of the job whose clean playlist it keeps updated.
    /// </summary>
    private async Task<IMusicService> ResolveMusicServiceAsync(PlaylistSyncConfig config)
    {
        var job = await _unitOfWork.Jobs.GetByIdAsync(config.OriginalJobId);
        if (job == null)
        {
            throw new InvalidOperationException(
                $"Sync config {config.Id} references job {config.OriginalJobId}, which no longer exists.");
        }

        return _musicServiceFactory.GetService(job.TargetProvider);
    }

    private async Task<List<TrackMapping>> ProcessNewTracksAsync(
        IMusicService musicService, List<MusicTrack> newTracks, PlaylistSyncConfig config)
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
                var cleanTrack = await musicService.FindCleanVersionAsync(config.UserId, track, CancellationToken.None);
                var mapping = new TrackMapping
                {
                    JobId = config.OriginalJobId,
                    SourceTrackId = track.Id,
                    SourceTrackName = track.Name,
                    SourceArtistName = string.Join(", ", track.Artists.Select(a => a.Name)),
                    IsExplicit = track.IsExplicit,
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

    /// <summary>
    /// Applies the delta to the clean playlist. Additive only.
    /// </summary>
    /// <remarks>
    /// Sync never removes tracks, and this is a permanent constraint rather than an
    /// unfinished feature. Apple Music's REST API offers no way to remove an item from a
    /// library playlist — it exposes only additions to the Cloud Library and to editable
    /// playlists. The one removal path Apple provides is native MusicKit rewriting the
    /// playlist's whole item list, which is available only on Apple platforms and only for
    /// playlists the app itself created; a .NET service using MusicKit JS cannot reach it.
    ///
    /// So <see cref="PlaylistDelta.TracksToRemove"/> is deliberately not acted on. Do not
    /// "fix" this by adding a remove method to <see cref="IMusicService"/> — the interface
    /// is add-only by design. The UI states plainly that removals do not propagate.
    /// </remarks>
    private async Task ApplyDeltaToPlaylistAsync(
        IMusicService musicService, PlaylistSyncConfig config, PlaylistDelta delta, List<TrackMapping> newMappings)
    {
        // Add clean versions of new tracks. The adapter owns any provider-specific ID or
        // URI formatting, so bare track IDs are passed through.
        var trackIdsToAdd = delta.TracksToAdd.ToList();
        trackIdsToAdd.AddRange(newMappings.Where(m => m.HasCleanMatch && !string.IsNullOrEmpty(m.TargetTrackId)).Select(m => m.TargetTrackId!));

        if (trackIdsToAdd.Any())
        {
            _logger.LogInformation("Adding {TrackCount} tracks to playlist {PlaylistId}", trackIdsToAdd.Count, config.TargetPlaylistId);
            await musicService.AddTracksToPlaylistAsync(
                config.UserId, config.TargetPlaylistId, trackIdsToAdd, CancellationToken.None);
        }
    }
}
