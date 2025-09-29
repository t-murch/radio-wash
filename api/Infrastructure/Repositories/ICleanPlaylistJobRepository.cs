using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface ICleanPlaylistJobRepository
{
  Task<CleanPlaylistJob?> GetByIdAsync(int jobId);
  Task<CleanPlaylistJob> CreateAsync(CleanPlaylistJob job);
  Task<CleanPlaylistJob> UpdateAsync(CleanPlaylistJob job);
  Task UpdateStatusAsync(int jobId, string status);
  Task UpdateProgressAsync(int jobId, int processedTracks, string? currentBatch = null);
  Task UpdateErrorAsync(int jobId, string errorMessage);
  Task SaveChangesAsync();
}
