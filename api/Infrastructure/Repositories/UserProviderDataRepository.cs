using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class UserProviderDataRepository : IUserProviderDataRepository
{
  private readonly RadioWashDbContext _dbContext;

  public UserProviderDataRepository(RadioWashDbContext dbContext)
  {
    _dbContext = dbContext;
  }

  public async Task<UserProviderData?> GetByProviderAsync(string provider, string providerId)
  {
    return await _dbContext.UserProviderData
        .Include(upd => upd.User)
        .FirstOrDefaultAsync(upd => upd.Provider == provider && upd.ProviderId == providerId);
  }

  public async Task<UserProviderData?> GetByUserAndProviderAsync(int userId, string provider)
  {
    return await _dbContext.UserProviderData
        .FirstOrDefaultAsync(upd => upd.UserId == userId && upd.Provider == provider);
  }

  public async Task<IEnumerable<UserProviderData>> GetByUserIdAsync(int userId)
  {
    return await _dbContext.UserProviderData
        .Where(upd => upd.UserId == userId)
        .ToListAsync();
  }

  public async Task<UserProviderData> CreateAsync(UserProviderData providerData)
  {
    _dbContext.UserProviderData.Add(providerData);
    await _dbContext.SaveChangesAsync();
    return providerData;
  }

  public async Task<UserProviderData> UpdateAsync(UserProviderData providerData)
  {
    providerData.UpdatedAt = DateTime.UtcNow;
    _dbContext.UserProviderData.Update(providerData);
    await _dbContext.SaveChangesAsync();
    return providerData;
  }

  public async Task DeleteAsync(UserProviderData providerData)
  {
    _dbContext.UserProviderData.Remove(providerData);
    await _dbContext.SaveChangesAsync();
  }

  public async Task SaveChangesAsync()
  {
    await _dbContext.SaveChangesAsync();
  }
}
