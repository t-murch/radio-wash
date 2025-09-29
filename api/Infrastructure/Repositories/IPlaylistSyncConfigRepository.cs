using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface IPlaylistSyncConfigRepository
{
    Task<PlaylistSyncConfig?> GetByIdAsync(int configId);
    Task<PlaylistSyncConfig?> GetByJobIdAsync(int jobId);
    Task<IEnumerable<PlaylistSyncConfig>> GetByUserIdAsync(int userId);
    Task<IEnumerable<PlaylistSyncConfig>> GetDueForSyncAsync(DateTime currentTime);
    Task<IEnumerable<PlaylistSyncConfig>> GetActiveConfigsAsync();
    Task<PlaylistSyncConfig> CreateAsync(PlaylistSyncConfig config);
    Task<PlaylistSyncConfig> UpdateAsync(PlaylistSyncConfig config);
    Task UpdateLastSyncAsync(int configId, DateTime syncTime, string status, string? error = null);
    Task UpdateNextScheduledSyncAsync(int configId, DateTime nextSync);
    Task DisableConfigAsync(int configId);
    Task SaveChangesAsync();
}