using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Simplified interface focusing on business operations only
/// </summary>
public interface ICleanPlaylistService
{
  Task<CleanPlaylistJobDto> CreateJobAsync(int userId, CreateCleanPlaylistJobDto jobDto);
  Task<JobProgress> GetJobProgressAsync(int jobId);
}
