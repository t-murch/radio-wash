using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RadioWash.Api.Hubs;

[Authorize]
public class JobStatusHub : Hub
{
  private readonly ILogger<JobStatusHub> _logger;

  public JobStatusHub(ILogger<JobStatusHub> logger)
  {
    _logger = logger;
  }

  public override async Task OnConnectedAsync()
  {
    var userId = Context.UserIdentifier; // Will be set from JWT
    if (!string.IsNullOrEmpty(userId))
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
      _logger.LogInformation("User {UserId} connected to JobStatusHub", userId);
    }
    await base.OnConnectedAsync();
  }

  public override async Task OnDisconnectedAsync(Exception? exception)
  {
    var userId = Context.UserIdentifier;
    if (!string.IsNullOrEmpty(userId))
    {
      await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
      _logger.LogInformation("User {UserId} disconnected from JobStatusHub", userId);
    }
    await base.OnDisconnectedAsync(exception);
  }

  public async Task SubscribeToJob(int jobId)
  {
    await Groups.AddToGroupAsync(Context.ConnectionId, $"job-{jobId}");
    _logger.LogInformation("Connection {ConnectionId} subscribed to job {JobId}", Context.ConnectionId, jobId);
  }

  public async Task UnsubscribeFromJob(int jobId)
  {
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job-{jobId}");
    _logger.LogInformation("Connection {ConnectionId} unsubscribed from job {JobId}", Context.ConnectionId, jobId);
  }
}

// DTOs for job updates
public class JobUpdateDto
{
  public int JobId { get; set; }
  public string Status { get; set; } = null!;
  public int ProcessedTracks { get; set; }
  public int TotalTracks { get; set; }
  public int MatchedTracks { get; set; }
  public string? ErrorMessage { get; set; }
  public DateTime UpdatedAt { get; set; }
}

public class TrackProcessedDto
{
  public int JobId { get; set; }
  public string SourceTrackName { get; set; } = null!;
  public string SourceArtistName { get; set; } = null!;
  public bool IsExplicit { get; set; }
  public bool HasCleanMatch { get; set; }
  public string? TargetTrackName { get; set; }
  public string? TargetArtistName { get; set; }
}
