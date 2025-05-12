using RadioWash.Api.Models.DTO;

namespace RadioWash.Api.Services.Interfaces;

public interface ICleanPlaylistService
{
  Task<CleanPlaylistJobDto> CreateJobAsync(int userId, CreateCleanPlaylistJobDto request);
  Task<CleanPlaylistJobDto> GetJobAsync(int userId, int jobId);
  Task<List<CleanPlaylistJobDto>> GetUserJobsAsync(int userId);
  Task ProcessJobAsync(int jobId);
  Task<List<TrackMappingDto>> GetJobTrackMappingsAsync(int userId, int jobId);
}
