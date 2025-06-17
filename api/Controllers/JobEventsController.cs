using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Hubs;

namespace RadioWash.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class JobEventsController : ControllerBase
{
    private readonly ILogger<JobEventsController> _logger;
    private readonly ICleanPlaylistService _cleanPlaylistService;
    private static readonly Dictionary<string, CancellationTokenSource> _connections = new();

    public JobEventsController(
        ILogger<JobEventsController> logger,
        ICleanPlaylistService cleanPlaylistService)
    {
        _logger = logger;
        _cleanPlaylistService = cleanPlaylistService;
    }

    [HttpGet("{jobId}")]
    public async Task StreamJobEvents(int jobId, CancellationToken cancellationToken)
    {
        var userId = GetUserIdFromClaims();
        var connectionId = Guid.NewGuid().ToString();
        
        try
        {
            // Validate user has access to this job
            var job = await _cleanPlaylistService.GetJobAsync(userId, jobId);
            
            // Set SSE headers
            Response.Headers["Content-Type"] = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Access-Control-Allow-Headers"] = "Cache-Control";

            // Register connection for cleanup
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _connections[connectionId] = cts;

            _logger.LogInformation("SSE connection established for user {UserId}, job {JobId}", userId, jobId);

            // Send initial job state
            await SendJobUpdate(job);
            
            // Send current track mappings if job is completed
            if (job.Status == "Completed" || job.Status == "Failed")
            {
                var trackMappings = await _cleanPlaylistService.GetJobTrackMappingsAsync(userId, jobId);
                foreach (var track in trackMappings)
                {
                    await SendTrackProcessed(jobId, track);
                }
            }

            // Keep connection alive with periodic heartbeats
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(30000, cts.Token);
                await SendEvent("heartbeat", new { jobId, timestamp = DateTime.UtcNow });
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SSE connection closed for user {UserId}, job {JobId}", userId, jobId);
        }
        catch (UnauthorizedAccessException)
        {
            Response.StatusCode = 401;
            await Response.WriteAsync("Unauthorized");
        }
        catch (Exception ex) when (ex.Message.Contains("Job not found"))
        {
            Response.StatusCode = 404;
            await Response.WriteAsync("Job not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSE stream for user {UserId}, job {JobId}", userId, jobId);
            Response.StatusCode = 500;
            await Response.WriteAsync("Internal server error");
        }
        finally
        {
            // Cleanup connection
            if (_connections.TryGetValue(connectionId, out var cts))
            {
                _connections.Remove(connectionId);
                cts.Dispose();
            }
        }
    }

    private int GetUserIdFromClaims()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }
        return userId;
    }

    private async Task SendJobUpdate(object jobData)
    {
        await SendEvent("job-status-changed", jobData);
    }

    private async Task SendTrackProcessed(int jobId, object trackData)
    {
        await SendEvent("track-processed", trackData);
    }

    private async Task SendEvent(string eventType, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            await Response.WriteAsync($"event: {eventType}\n");
            await Response.WriteAsync($"data: {json}\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SSE event {EventType}", eventType);
            throw;
        }
    }
}