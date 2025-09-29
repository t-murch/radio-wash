using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class PlaylistSyncHistoryRepository : IPlaylistSyncHistoryRepository
{
    private readonly RadioWashDbContext _dbContext;

    public PlaylistSyncHistoryRepository(RadioWashDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PlaylistSyncHistory?> GetByIdAsync(int historyId)
    {
        return await _dbContext.PlaylistSyncHistory
            .Include(psh => psh.SyncConfig)
            .ThenInclude(psc => psc.OriginalJob)
            .FirstOrDefaultAsync(psh => psh.Id == historyId);
    }

    public async Task<IEnumerable<PlaylistSyncHistory>> GetByConfigIdAsync(int configId, int limit = 50)
    {
        return await _dbContext.PlaylistSyncHistory
            .Where(psh => psh.SyncConfigId == configId)
            .OrderByDescending(psh => psh.StartedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<PlaylistSyncHistory>> GetRecentHistoryAsync(int userId, int limit = 20)
    {
        return await _dbContext.PlaylistSyncHistory
            .Include(psh => psh.SyncConfig)
            .ThenInclude(psc => psc.OriginalJob)
            .Where(psh => psh.SyncConfig.UserId == userId)
            .OrderByDescending(psh => psh.StartedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<PlaylistSyncHistory> CreateAsync(PlaylistSyncHistory history)
    {
        _dbContext.PlaylistSyncHistory.Add(history);
        await _dbContext.SaveChangesAsync();
        return history;
    }

    public async Task<PlaylistSyncHistory> UpdateAsync(PlaylistSyncHistory history)
    {
        _dbContext.PlaylistSyncHistory.Update(history);
        await _dbContext.SaveChangesAsync();
        return history;
    }

    public async Task CompleteHistoryAsync(int historyId, int tracksAdded, int tracksRemoved, int tracksUnchanged, int executionTimeMs)
    {
        var history = await _dbContext.PlaylistSyncHistory.FindAsync(historyId);
        if (history != null)
        {
            history.CompletedAt = DateTime.UtcNow;
            history.Status = SyncStatus.Completed;
            history.TracksAdded = tracksAdded;
            history.TracksRemoved = tracksRemoved;
            history.TracksUnchanged = tracksUnchanged;
            history.ExecutionTimeMs = executionTimeMs;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task FailHistoryAsync(int historyId, string errorMessage)
    {
        var history = await _dbContext.PlaylistSyncHistory.FindAsync(historyId);
        if (history != null)
        {
            history.CompletedAt = DateTime.UtcNow;
            history.Status = SyncStatus.Failed;
            history.ErrorMessage = errorMessage;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}