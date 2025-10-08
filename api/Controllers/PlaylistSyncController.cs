using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PlaylistSyncController : AuthenticatedControllerBase
{
  private readonly IPlaylistSyncService _syncService;
  private readonly ISubscriptionService _subscriptionService;
  private readonly ILogger<PlaylistSyncController> _logger;

  public PlaylistSyncController(
      RadioWashDbContext dbContext,
      IPlaylistSyncService syncService,
      ISubscriptionService subscriptionService,
      ILogger<PlaylistSyncController> logger) : base(dbContext, logger)
  {
    _syncService = syncService;
    _subscriptionService = subscriptionService;
    _logger = logger;
  }

  [HttpGet]
  public async Task<ActionResult<IEnumerable<PlaylistSyncConfigDto>>> GetSyncConfigs()
  {
    var userId = GetCurrentUserId();
    var configs = await _syncService.GetUserSyncConfigsAsync(userId);

    var configDtos = configs.Select(c => new PlaylistSyncConfigDto
    {
      Id = c.Id,
      OriginalJobId = c.OriginalJobId,
      SourcePlaylistId = c.SourcePlaylistId,
      SourcePlaylistName = c.OriginalJob?.SourcePlaylistName ?? "Unknown",
      TargetPlaylistId = c.TargetPlaylistId,
      TargetPlaylistName = c.OriginalJob?.TargetPlaylistName ?? "Unknown",
      IsActive = c.IsActive,
      SyncFrequency = c.SyncFrequency,
      LastSyncedAt = c.LastSyncedAt,
      LastSyncStatus = c.LastSyncStatus,
      LastSyncError = c.LastSyncError,
      NextScheduledSync = c.NextScheduledSync,
      CreatedAt = c.CreatedAt
    });

    return Ok(configDtos);
  }

  [HttpPost("enable")]
  public async Task<ActionResult<PlaylistSyncConfigDto>> EnableSync([FromBody] EnableSyncDto dto)
  {
    var userId = GetCurrentUserId();

    // Check if user has active subscription
    var hasActiveSubscription = await _subscriptionService.HasActiveSubscriptionAsync(userId);
    if (!hasActiveSubscription)
    {
      return BadRequest(new { error = "Active subscription required to enable sync" });
    }

    try
    {
      var config = await _syncService.EnableSyncForJobAsync(dto.JobId, userId);
      if (config == null)
      {
        return BadRequest(new { error = "Failed to enable sync" });
      }

      var configDto = new PlaylistSyncConfigDto
      {
        Id = config.Id,
        OriginalJobId = config.OriginalJobId,
        SourcePlaylistId = config.SourcePlaylistId,
        SourcePlaylistName = config.OriginalJob?.SourcePlaylistName ?? "Unknown",
        TargetPlaylistId = config.TargetPlaylistId,
        TargetPlaylistName = config.OriginalJob?.TargetPlaylistName ?? "Unknown",
        IsActive = config.IsActive,
        SyncFrequency = config.SyncFrequency,
        LastSyncedAt = config.LastSyncedAt,
        LastSyncStatus = config.LastSyncStatus,
        LastSyncError = config.LastSyncError,
        NextScheduledSync = config.NextScheduledSync,
        CreatedAt = config.CreatedAt
      };

      return Ok(configDto);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to enable sync for job {JobId}, user {UserId}", dto.JobId, userId);
      return BadRequest(new { error = ex.Message });
    }
  }

  [HttpDelete("{configId}")]
  public async Task<ActionResult> DisableSync(int configId)
  {
    var userId = GetCurrentUserId();
    var success = await _syncService.DisableSyncAsync(configId, userId);

    if (!success)
    {
      return NotFound(new { error = "Sync configuration not found" });
    }

    return Ok(new { message = "Sync disabled successfully" });
  }

  [HttpPatch("{configId}/frequency")]
  public async Task<ActionResult<PlaylistSyncConfigDto>> UpdateSyncFrequency(int configId, [FromBody] UpdateSyncFrequencyDto dto)
  {
    var userId = GetCurrentUserId();
    var config = await _syncService.UpdateSyncFrequencyAsync(configId, dto.Frequency, userId);

    if (config == null)
    {
      return NotFound(new { error = "Sync configuration not found" });
    }

    var configDto = new PlaylistSyncConfigDto
    {
      Id = config.Id,
      OriginalJobId = config.OriginalJobId,
      SourcePlaylistId = config.SourcePlaylistId,
      SourcePlaylistName = config.OriginalJob?.SourcePlaylistName ?? "Unknown",
      TargetPlaylistId = config.TargetPlaylistId,
      TargetPlaylistName = config.OriginalJob?.TargetPlaylistName ?? "Unknown",
      IsActive = config.IsActive,
      SyncFrequency = config.SyncFrequency,
      LastSyncedAt = config.LastSyncedAt,
      LastSyncStatus = config.LastSyncStatus,
      LastSyncError = config.LastSyncError,
      NextScheduledSync = config.NextScheduledSync,
      CreatedAt = config.CreatedAt
    };

    return Ok(configDto);
  }

  [HttpPost("{configId}/sync")]
  public async Task<ActionResult<SyncResultDto>> ManualSync(int configId)
  {
    var userId = GetCurrentUserId();

    try
    {
      var result = await _syncService.ManualSyncAsync(configId, userId);

      var resultDto = new SyncResultDto
      {
        Success = result.Success,
        TracksAdded = result.TracksAdded,
        TracksRemoved = result.TracksRemoved,
        TracksUnchanged = result.TracksUnchanged,
        ErrorMessage = result.ErrorMessage,
        ExecutionTimeMs = (long)result.ExecutionTime.TotalMilliseconds
      };

      return Ok(resultDto);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Manual sync failed for config {ConfigId}, user {UserId}", configId, userId);
      return BadRequest(new { error = ex.Message });
    }
  }

  [HttpGet("{configId}/history")]
  public async Task<ActionResult<IEnumerable<PlaylistSyncHistoryDto>>> GetSyncHistory(int configId, [FromQuery] int limit = 20)
  {
    var userId = GetCurrentUserId();

    // Verify user owns this config (basic security check)
    var configs = await _syncService.GetUserSyncConfigsAsync(userId);
    if (!configs.Any(c => c.Id == configId))
    {
      return NotFound(new { error = "Sync configuration not found" });
    }

    var history = await _syncService.GetSyncHistoryAsync(configId, limit);

    var historyDtos = history.Select(h => new PlaylistSyncHistoryDto
    {
      Id = h.Id,
      StartedAt = h.StartedAt,
      CompletedAt = h.CompletedAt,
      Status = h.Status,
      TracksAdded = h.TracksAdded,
      TracksRemoved = h.TracksRemoved,
      TracksUnchanged = h.TracksUnchanged,
      ErrorMessage = h.ErrorMessage,
      ExecutionTimeMs = h.ExecutionTimeMs
    });

    return Ok(historyDtos);
  }
}
