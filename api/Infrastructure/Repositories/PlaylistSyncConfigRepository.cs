using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class PlaylistSyncConfigRepository : IPlaylistSyncConfigRepository
{
  private readonly RadioWashDbContext _dbContext;

  public PlaylistSyncConfigRepository(RadioWashDbContext dbContext)
  {
    _dbContext = dbContext;
  }

  public async Task<PlaylistSyncConfig?> GetByIdAsync(int configId)
  {
    return await _dbContext.PlaylistSyncConfigs
        .Include(psc => psc.User)
        .Include(psc => psc.OriginalJob)
        .FirstOrDefaultAsync(psc => psc.Id == configId);
  }

  public async Task<PlaylistSyncConfig?> GetByJobIdAsync(int jobId)
  {
    return await _dbContext.PlaylistSyncConfigs
        .Include(psc => psc.User)
        .Include(psc => psc.OriginalJob)
        .FirstOrDefaultAsync(psc => psc.OriginalJobId == jobId);
  }

  public async Task<IEnumerable<PlaylistSyncConfig>> GetByUserIdAsync(int userId)
  {
    return await _dbContext.PlaylistSyncConfigs
        .Include(psc => psc.OriginalJob)
        .Where(psc => psc.UserId == userId)
        .OrderByDescending(psc => psc.CreatedAt)
        .ToListAsync();
  }

  public async Task<IEnumerable<PlaylistSyncConfig>> GetDueForSyncAsync(DateTime currentTime)
  {
    return await _dbContext.PlaylistSyncConfigs
        .Include(psc => psc.User)
        .Include(psc => psc.OriginalJob)
        .Where(psc => psc.IsActive &&
                     psc.NextScheduledSync.HasValue &&
                     psc.NextScheduledSync.Value <= currentTime)
        .ToListAsync();
  }

  public async Task<IEnumerable<PlaylistSyncConfig>> GetActiveConfigsAsync()
  {
    return await _dbContext.PlaylistSyncConfigs
        .Include(psc => psc.User)
        .Include(psc => psc.OriginalJob)
        .Where(psc => psc.IsActive)
        .ToListAsync();
  }

  public async Task<PlaylistSyncConfig> CreateAsync(PlaylistSyncConfig config)
  {
    _dbContext.PlaylistSyncConfigs.Add(config);
    await _dbContext.SaveChangesAsync();
    return config;
  }

  public async Task<PlaylistSyncConfig> UpdateAsync(PlaylistSyncConfig config)
  {
    config.UpdatedAt = DateTime.UtcNow;
    _dbContext.PlaylistSyncConfigs.Update(config);
    await _dbContext.SaveChangesAsync();
    return config;
  }

  public async Task UpdateLastSyncAsync(int configId, DateTime syncTime, string status, string? error = null)
  {
    var config = await _dbContext.PlaylistSyncConfigs.FindAsync(configId);
    if (config != null)
    {
      config.LastSyncedAt = syncTime;
      config.LastSyncStatus = status;
      config.LastSyncError = error;
      config.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();
    }
  }

  public async Task UpdateNextScheduledSyncAsync(int configId, DateTime nextSync)
  {
    var config = await _dbContext.PlaylistSyncConfigs.FindAsync(configId);
    if (config != null)
    {
      config.NextScheduledSync = nextSync;
      config.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();
    }
  }

  public async Task DisableConfigAsync(int configId)
  {
    var config = await _dbContext.PlaylistSyncConfigs.FindAsync(configId);
    if (config != null)
    {
      config.IsActive = false;
      config.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();
    }
  }

  public async Task SaveChangesAsync()
  {
    await _dbContext.SaveChangesAsync();
  }
}
