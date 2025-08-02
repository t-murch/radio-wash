using RadioWash.Api.Models;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Service for broadcasting real-time progress updates to connected clients
/// </summary>
public interface IProgressBroadcastService
{
    /// <summary>
    /// Broadcasts progress update to all clients subscribed to a specific job
    /// </summary>
    /// <param name="jobId">The job ID to broadcast to</param>
    /// <param name="update">The progress update to broadcast</param>
    Task BroadcastProgressUpdate(int jobId, ProgressUpdate update);

    /// <summary>
    /// Notifies clients that a job has completed successfully
    /// </summary>
    /// <param name="jobId">The completed job ID</param>
    /// <param name="message">Optional completion message</param>
    Task BroadcastJobCompleted(int jobId, string? message = null);

    /// <summary>
    /// Notifies clients that a job has failed
    /// </summary>
    /// <param name="jobId">The failed job ID</param>
    /// <param name="error">Error message</param>
    Task BroadcastJobFailed(int jobId, string error);
}