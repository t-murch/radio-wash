using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface IPlaylistSyncHistoryRepository
{
  Task<PlaylistSyncHistory?> GetByIdAsync(int historyId);
  Task<IEnumerable<PlaylistSyncHistory>> GetByConfigIdAsync(int configId, int limit = 50);
  Task<IEnumerable<PlaylistSyncHistory>> GetRecentHistoryAsync(int userId, int limit = 20);
  Task<PlaylistSyncHistory> CreateAsync(PlaylistSyncHistory history);
  Task<PlaylistSyncHistory> UpdateAsync(PlaylistSyncHistory history);
  Task CompleteHistoryAsync(int historyId, int tracksAdded, int tracksRemoved, int tracksUnchanged, int executionTimeMs);
  Task FailHistoryAsync(int historyId, string errorMessage);
  Task SaveChangesAsync();
}
