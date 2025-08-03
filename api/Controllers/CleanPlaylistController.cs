using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;
using System.Security.Claims;

namespace RadioWash.Api.Controllers;

[Route("api/[controller]")]
public class CleanPlaylistController : AuthenticatedControllerBase
{
  private readonly ICleanPlaylistService _cleanPlaylistService;
  private readonly RadioWashDbContext _dbContext;

  public CleanPlaylistController(ICleanPlaylistService cleanPlaylistService, RadioWashDbContext dbContext, ILogger<CleanPlaylistController> logger)
    : base(dbContext, logger)
  {
    _cleanPlaylistService = cleanPlaylistService;
    _dbContext = dbContext;
  }

  [HttpPost("user/{userId:int}/job")]
  public async Task<IActionResult> CreateCleanPlaylistJob(int userId, [FromBody] CreateCleanPlaylistJobDto jobDto)
  {
    // Basic authorization check: ensure the user ID in the path matches the one from the token.
    var authenticatedUserId = GetCurrentUserId();
    if (authenticatedUserId != userId)
    {
      return Forbid();
    }

    try
    {
      var createdJob = await _cleanPlaylistService.CreateJobAsync(userId, jobDto);
      return CreatedAtAction(nameof(GetJob), new { userId, jobId = createdJob.Id }, createdJob);
    }
    catch (KeyNotFoundException ex)
    {
      return NotFound(new { message = ex.Message });
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error creating clean playlist job for user {UserId}", userId);
      return StatusCode(500, "An internal error occurred.");
    }
  }

  [HttpGet("user/me/jobs")]
  public async Task<IActionResult> GetUserJobs()
  {
    var userId = GetCurrentUserId();

    var jobs = await _dbContext.CleanPlaylistJobs
        .Where(j => j.UserId == userId)
        .OrderByDescending(j => j.CreatedAt)
        .Select(job => new CleanPlaylistJobDto
        {
          Id = job.Id,
          SourcePlaylistId = job.SourcePlaylistId,
          SourcePlaylistName = job.SourcePlaylistName,
          TargetPlaylistId = job.TargetPlaylistId,
          TargetPlaylistName = job.TargetPlaylistName,
          Status = job.Status,
          TotalTracks = job.TotalTracks,
          ProcessedTracks = job.ProcessedTracks,
          MatchedTracks = job.MatchedTracks,
          CreatedAt = job.CreatedAt,
          UpdatedAt = job.UpdatedAt
        })
        .ToListAsync();

    return Ok(jobs);
  }

  [HttpGet("user/{userId:int}/job/{jobId:int}")]
  public async Task<IActionResult> GetJob(int userId, int jobId)
  {
    var authenticatedUserId = GetCurrentUserId();
    if (authenticatedUserId != userId)
    {
      return Forbid();
    }

    var job = await _dbContext.CleanPlaylistJobs
        .Where(j => j.UserId == userId && j.Id == jobId)
        .Select(j => new CleanPlaylistJobDto
        {
          Id = j.Id,
          SourcePlaylistId = j.SourcePlaylistId,
          SourcePlaylistName = j.SourcePlaylistName,
          TargetPlaylistId = j.TargetPlaylistId,
          TargetPlaylistName = j.TargetPlaylistName,
          Status = j.Status,
          TotalTracks = j.TotalTracks,
          ProcessedTracks = j.ProcessedTracks,
          MatchedTracks = j.MatchedTracks,
          ErrorMessage = j.ErrorMessage,
          CreatedAt = j.CreatedAt,
          UpdatedAt = j.UpdatedAt
        })
        .FirstOrDefaultAsync();

    if (job == null)
    {
      return NotFound();
    }

    return Ok(job);
  }

  [HttpGet("user/{userId:int}/job/{jobId:int}/tracks")]
  public async Task<IActionResult> GetJobTrackMappings(int userId, int jobId)
  {
    var authenticatedUserId = GetCurrentUserId();
    if (authenticatedUserId != userId)
    {
      return Forbid();
    }

    var jobExists = await _dbContext.CleanPlaylistJobs.AnyAsync(j => j.Id == jobId && j.UserId == userId);
    if (!jobExists)
    {
      return NotFound("Job not found or you do not have access.");
    }

    var mappings = await _dbContext.TrackMappings
        .Where(t => t.JobId == jobId)
        .Select(t => new TrackMappingDto
        {
          Id = t.Id,
          SourceTrackId = t.SourceTrackId,
          SourceTrackName = t.SourceTrackName,
          SourceArtistName = t.SourceArtistName,
          IsExplicit = t.IsExplicit,
          TargetTrackId = t.TargetTrackId,
          TargetTrackName = t.TargetTrackName,
          TargetArtistName = t.TargetArtistName,
          HasCleanMatch = t.HasCleanMatch
        })
        .ToListAsync();

    return Ok(mappings);
  }

}
