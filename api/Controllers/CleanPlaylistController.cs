using Hangfire;
using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CleanPlaylistController : ControllerBase
{
  private readonly ICleanPlaylistService _cleanPlaylistService;
  private readonly ILogger<CleanPlaylistController> _logger;

  public CleanPlaylistController(
      ICleanPlaylistService cleanPlaylistService,
      ILogger<CleanPlaylistController> logger)
  {
    _cleanPlaylistService = cleanPlaylistService;
    _logger = logger;
  }

  /// <summary>
  /// Creates a new job to clean a playlist
  /// </summary>
  [HttpPost("user/{userId}/job")]
  public async Task<IActionResult> CreateJob(int userId, CreateCleanPlaylistJobDto request)
  {
    try
    {
      var job = await _cleanPlaylistService.CreateJobAsync(userId, request);
      return CreatedAtAction(nameof(GetJob), new { userId, jobId = job.Id }, job);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error creating clean playlist job for user {UserId}", userId);
      return StatusCode(500, new { error = "Failed to create clean playlist job" });
    }
  }

  /// <summary>
  /// Gets a specific job by ID
  /// </summary>
  [HttpGet("user/{userId}/job/{jobId}")]
  public async Task<IActionResult> GetJob(int userId, int jobId)
  {
    try
    {
      var job = await _cleanPlaylistService.GetJobAsync(userId, jobId);
      return Ok(job);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting job {JobId} for user {UserId}", jobId, userId);
      return StatusCode(500, new { error = "Failed to get job" });
    }
  }

  /// <summary>
  /// Gets all jobs for a user
  /// </summary>
  [HttpGet("user/{userId}/jobs")]
  public async Task<IActionResult> GetUserJobs(int userId)
  {
    try
    {
      var jobs = await _cleanPlaylistService.GetUserJobsAsync(userId);
      return Ok(jobs);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting jobs for user {UserId}", userId);
      return StatusCode(500, new { error = "Failed to get jobs" });
    }
  }

  /// <summary>
  /// Gets all track mappings for a job
  /// </summary>
  [HttpGet("user/{userId}/job/{jobId}/tracks")]
  public async Task<IActionResult> GetJobTrackMappings(int userId, int jobId)
  {
    try
    {
      var trackMappings = await _cleanPlaylistService.GetJobTrackMappingsAsync(userId, jobId);
      return Ok(trackMappings);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting track mappings for job {JobId}", jobId);
      return StatusCode(500, new { error = "Failed to get track mappings" });
    }
  }

  /// <summary>
  /// Manually triggers processing of a job (for testing purposes)
  /// </summary>
  [HttpPost("job/{jobId}/process")]
  public IActionResult ProcessJob(int jobId)
  {
    try
    {
      BackgroundJob.Enqueue(() => _cleanPlaylistService.ProcessJobAsync(jobId));
      return Ok(new { message = "Job processing started" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing job {JobId}", jobId);
      return StatusCode(500, new { error = "Failed to process job" });
    }
  }
}
