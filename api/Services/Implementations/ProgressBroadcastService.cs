using Microsoft.AspNetCore.SignalR;
using RadioWash.Api.Hubs;
using RadioWash.Api.Models;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Service for broadcasting real-time progress updates via SignalR
/// </summary>
public class ProgressBroadcastService : IProgressBroadcastService
{
    private readonly IHubContext<PlaylistProgressHub, IPlaylistProgressClient> _hubContext;
    private readonly ILogger<ProgressBroadcastService> _logger;

    public ProgressBroadcastService(
        IHubContext<PlaylistProgressHub, IPlaylistProgressClient> hubContext,
        ILogger<ProgressBroadcastService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Broadcasts progress update to all clients subscribed to a specific job
    /// </summary>
    /// <param name="jobId">The job ID to broadcast to</param>
    /// <param name="update">The progress update to broadcast</param>
    public async Task BroadcastProgressUpdate(int jobId, ProgressUpdate update)
    {
        try
        {
            var groupName = $"job_{jobId}";
            await _hubContext.Clients.Group(groupName).ProgressUpdate(update);

            _logger.LogInformation("Broadcasted progress update for job {JobId}: {Progress}% - {Message}", 
                jobId, update.Progress, update.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast progress update for job {JobId}", jobId);
        }
    }

    /// <summary>
    /// Notifies clients that a job has completed successfully
    /// </summary>
    /// <param name="jobId">The completed job ID</param>
    /// <param name="message">Optional completion message</param>
    public async Task BroadcastJobCompleted(int jobId, string? message = null)
    {
        try
        {
            var groupName = $"job_{jobId}";
            await _hubContext.Clients.Group(groupName).JobCompleted(jobId, true, message);

            _logger.LogInformation("Broadcasted job completion for job {JobId}: {Message}", jobId, message ?? "Success");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast job completion for job {JobId}", jobId);
        }
    }

    /// <summary>
    /// Notifies clients that a job has failed
    /// </summary>
    /// <param name="jobId">The failed job ID</param>
    /// <param name="error">Error message</param>
    public async Task BroadcastJobFailed(int jobId, string error)
    {
        try
        {
            var groupName = $"job_{jobId}";
            await _hubContext.Clients.Group(groupName).JobFailed(jobId, error);

            _logger.LogWarning("Broadcasted job failure for job {JobId}: {Error}", jobId, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast job failure for job {JobId}", jobId);
        }
    }
}