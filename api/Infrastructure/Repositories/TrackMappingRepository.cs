using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class TrackMappingRepository : ITrackMappingRepository
{
  private readonly RadioWashDbContext _dbContext;

  public TrackMappingRepository(RadioWashDbContext dbContext)
  {
    _dbContext = dbContext;
  }

  public async Task<IEnumerable<TrackMapping>> GetByJobIdAsync(int jobId)
  {
    return await _dbContext.TrackMappings
        .Where(tm => tm.JobId == jobId)
        .ToListAsync();
  }

  public async Task<TrackMapping> CreateAsync(TrackMapping trackMapping)
  {
    _dbContext.TrackMappings.Add(trackMapping);
    await _dbContext.SaveChangesAsync();
    return trackMapping;
  }

  public async Task AddAsync(TrackMapping trackMapping)
  {
    _dbContext.TrackMappings.Add(trackMapping);
    // Note: SaveChangesAsync is not called here - caller should call it
    await Task.CompletedTask;
  }

  public async Task AddRangeAsync(IEnumerable<TrackMapping> trackMappings)
  {
    await _dbContext.TrackMappings.AddRangeAsync(trackMappings);
    await _dbContext.SaveChangesAsync();
  }

  public async Task<TrackMapping> UpdateAsync(TrackMapping trackMapping)
  {
    _dbContext.TrackMappings.Update(trackMapping);
    await _dbContext.SaveChangesAsync();
    return trackMapping;
  }

  public async Task DeleteAsync(TrackMapping trackMapping)
  {
    _dbContext.TrackMappings.Remove(trackMapping);
    await _dbContext.SaveChangesAsync();
  }

  public async Task SaveChangesAsync()
  {
    await _dbContext.SaveChangesAsync();
  }
}
