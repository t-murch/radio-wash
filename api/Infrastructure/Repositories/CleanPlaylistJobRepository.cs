using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class CleanPlaylistJobRepository : ICleanPlaylistJobRepository
{
    private readonly RadioWashDbContext _dbContext;

    public CleanPlaylistJobRepository(RadioWashDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CleanPlaylistJob?> GetByIdAsync(int jobId)
    {
        return await _dbContext.CleanPlaylistJobs
            .Include(j => j.User)
            .Include(j => j.TrackMappings)
            .FirstOrDefaultAsync(j => j.Id == jobId);
    }

    public async Task<CleanPlaylistJob> CreateAsync(CleanPlaylistJob job)
    {
        _dbContext.CleanPlaylistJobs.Add(job);
        await _dbContext.SaveChangesAsync();
        return job;
    }

    public async Task<CleanPlaylistJob> UpdateAsync(CleanPlaylistJob job)
    {
        job.UpdatedAt = DateTime.UtcNow;
        _dbContext.CleanPlaylistJobs.Update(job);
        await _dbContext.SaveChangesAsync();
        return job;
    }

    public async Task UpdateStatusAsync(int jobId, string status)
    {
        var job = await _dbContext.CleanPlaylistJobs.FindAsync(jobId);
        if (job != null)
        {
            job.Status = status;
            job.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateProgressAsync(int jobId, int processedTracks, string? currentBatch = null)
    {
        var job = await _dbContext.CleanPlaylistJobs.FindAsync(jobId);
        if (job != null)
        {
            job.ProcessedTracks = processedTracks;
            if (!string.IsNullOrEmpty(currentBatch))
            {
                job.CurrentBatch = currentBatch;
            }
            job.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateErrorAsync(int jobId, string errorMessage)
    {
        var job = await _dbContext.CleanPlaylistJobs.FindAsync(jobId);
        if (job != null)
        {
            job.ErrorMessage = errorMessage;
            job.Status = "Failed";
            job.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}