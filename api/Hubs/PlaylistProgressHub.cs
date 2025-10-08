using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using RadioWash.Api.Models;

namespace RadioWash.Api.Hubs;

/// <summary>
/// SignalR Hub for real-time playlist creation progress updates
/// </summary>
[Authorize]
public class PlaylistProgressHub : Hub<IPlaylistProgressClient>
{
  private readonly ILogger<PlaylistProgressHub> _logger;

  public PlaylistProgressHub(ILogger<PlaylistProgressHub> logger)
  {
    _logger = logger;
  }
  /// <summary>
  /// Joins a job-specific group to receive progress updates for that job
  /// </summary>
  /// <param name="jobId">The job ID to subscribe to</param>
  public async Task JoinJobGroup(string jobId)
  {
    var groupName = $"job_{jobId}";
    _logger.LogInformation("Client {ConnectionId} joining group {GroupName} for job {JobId}. User: {UserId}",
        Context.ConnectionId, groupName, jobId, Context.User?.Identity?.Name ?? "Unknown");

    try
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
      _logger.LogInformation("Client {ConnectionId} successfully joined group {GroupName}",
          Context.ConnectionId, groupName);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to add client {ConnectionId} to group {GroupName}",
          Context.ConnectionId, groupName);
      throw;
    }
  }

  /// <summary>
  /// Leaves a job-specific group to stop receiving progress updates
  /// </summary>
  /// <param name="jobId">The job ID to unsubscribe from</param>
  public async Task LeaveJobGroup(string jobId)
  {
    var groupName = $"job_{jobId}";
    _logger.LogInformation("Client {ConnectionId} leaving group {GroupName} for job {JobId}",
        Context.ConnectionId, groupName, jobId);

    try
    {
      await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
      _logger.LogInformation("Client {ConnectionId} successfully left group {GroupName}",
          Context.ConnectionId, groupName);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to remove client {ConnectionId} from group {GroupName}",
          Context.ConnectionId, groupName);
      throw;
    }
  }

  /// <summary>
  /// Called when a client connects to the hub
  /// </summary>
  public override async Task OnConnectedAsync()
  {
    _logger.LogInformation("Client connected to PlaylistProgressHub. ConnectionId: {ConnectionId}, User: {UserId}, UserAgent: {UserAgent}",
        Context.ConnectionId,
        Context.User?.Identity?.Name ?? "Unknown",
        Context.GetHttpContext()?.Request.Headers.UserAgent.ToString() ?? "Unknown");

    await base.OnConnectedAsync();
  }

  /// <summary>
  /// Called when a client disconnects from the hub
  /// </summary>
  public override async Task OnDisconnectedAsync(Exception? exception)
  {
    if (exception != null)
    {
      _logger.LogWarning(exception, "Client {ConnectionId} disconnected with error: {Error}",
          Context.ConnectionId, exception.Message);
    }
    else
    {
      _logger.LogInformation("Client {ConnectionId} disconnected normally", Context.ConnectionId);
    }

    await base.OnDisconnectedAsync(exception);
  }
}

/// <summary>
/// Interface for strongly-typed SignalR clients
/// </summary>
public interface IPlaylistProgressClient
{
  /// <summary>
  /// Sends a progress update to the client
  /// </summary>
  /// <param name="update">The progress update containing percentage, processed tracks, etc.</param>
  Task ProgressUpdate(ProgressUpdate update);

  /// <summary>
  /// Notifies the client that a job has completed
  /// </summary>
  /// <param name="jobId">The completed job ID</param>
  /// <param name="success">Whether the job completed successfully</param>
  /// <param name="message">Optional completion message</param>
  Task JobCompleted(int jobId, bool success, string? message = null);

  /// <summary>
  /// Notifies the client that a job has failed
  /// </summary>
  /// <param name="jobId">The failed job ID</param>
  /// <param name="error">Error message</param>
  Task JobFailed(int jobId, string error);
}
