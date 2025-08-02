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
    /// <summary>
    /// Joins a job-specific group to receive progress updates for that job
    /// </summary>
    /// <param name="jobId">The job ID to subscribe to</param>
    public async Task JoinJobGroup(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"job_{jobId}");
    }

    /// <summary>
    /// Leaves a job-specific group to stop receiving progress updates
    /// </summary>
    /// <param name="jobId">The job ID to unsubscribe from</param>
    public async Task LeaveJobGroup(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job_{jobId}");
    }

    /// <summary>
    /// Called when a client connects to the hub
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
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