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
    var groupName = $"job_{jobId}";
    _logger.LogInformation("Attempting to broadcast progress update for job {JobId} to group {GroupName}. Update: {Progress}% - {Message}, ProcessedTracks: {ProcessedTracks}/{TotalTracks}",
        jobId, groupName, update.Progress, update.Message, update.ProcessedTracks, update.TotalTracks);

    try
    {
      var startTime = DateTime.UtcNow;
      await _hubContext.Clients.Group(groupName).ProgressUpdate(update);
      var endTime = DateTime.UtcNow;
      var duration = (endTime - startTime).TotalMilliseconds;

      _logger.LogInformation("Successfully broadcasted progress update for job {JobId} in {Duration}ms. Group: {GroupName}, Progress: {Progress}%",
          jobId, duration, groupName, update.Progress);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to broadcast progress update for job {JobId} to group {GroupName}. Update details: {Update}",
          jobId, groupName, System.Text.Json.JsonSerializer.Serialize(update));
    }
  }

  /// <summary>
  /// Notifies clients that a job has completed successfully
  /// </summary>
  /// <param name="jobId">The completed job ID</param>
  /// <param name="message">Optional completion message</param>
  public async Task BroadcastJobCompleted(int jobId, string? message = null)
  {
    var groupName = $"job_{jobId}";
    _logger.LogInformation("Attempting to broadcast job completion for job {JobId} to group {GroupName}. Message: {Message}",
        jobId, groupName, message ?? "Success");

    try
    {
      var startTime = DateTime.UtcNow;
      await _hubContext.Clients.Group(groupName).JobCompleted(jobId, true, message);
      var endTime = DateTime.UtcNow;
      var duration = (endTime - startTime).TotalMilliseconds;

      _logger.LogInformation("Successfully broadcasted job completion for job {JobId} in {Duration}ms. Group: {GroupName}",
          jobId, duration, groupName);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to broadcast job completion for job {JobId} to group {GroupName}. Message: {Message}",
          jobId, groupName, message);
    }
  }

  /// <summary>
  /// Notifies clients that a job has failed
  /// </summary>
  /// <param name="jobId">The failed job ID</param>
  /// <param name="error">Error message</param>
  public async Task BroadcastJobFailed(int jobId, string error)
  {
    var groupName = $"job_{jobId}";
    _logger.LogInformation("Attempting to broadcast job failure for job {JobId} to group {GroupName}. Error: {Error}",
        jobId, groupName, error);

    try
    {
      var startTime = DateTime.UtcNow;
      await _hubContext.Clients.Group(groupName).JobFailed(jobId, error);
      var endTime = DateTime.UtcNow;
      var duration = (endTime - startTime).TotalMilliseconds;

      _logger.LogWarning("Successfully broadcasted job failure for job {JobId} in {Duration}ms. Group: {GroupName}, Error: {Error}",
          jobId, duration, groupName, error);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to broadcast job failure for job {JobId} to group {GroupName}. Error: {Error}",
          jobId, groupName, error);
    }
  }
}
