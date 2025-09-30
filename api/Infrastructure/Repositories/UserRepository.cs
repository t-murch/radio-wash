using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
  private readonly RadioWashDbContext _dbContext;

  public UserRepository(RadioWashDbContext dbContext)
  {
    _dbContext = dbContext;
  }

  public async Task<User?> GetBySupabaseIdAsync(string supabaseId)
  {
    return await _dbContext.Users
        .Include(u => u.ProviderData)
        .FirstOrDefaultAsync(u => u.SupabaseId == supabaseId);
  }

  public async Task<User?> GetByIdAsync(int userId)
  {
    return await _dbContext.Users
        .Include(u => u.ProviderData)
        .FirstOrDefaultAsync(u => u.Id == userId);
  }

  public async Task<User?> GetByEmailAsync(string email)
  {
    return await _dbContext.Users
        .Include(u => u.ProviderData)
        .FirstOrDefaultAsync(u => u.Email == email);
  }

  public async Task<User?> GetByProviderAsync(string provider, string providerId)
  {
    var userProviderData = await _dbContext.Set<UserProviderData>()
        .Include(upd => upd.User)
        .ThenInclude(u => u.ProviderData)
        .FirstOrDefaultAsync(upd => upd.Provider == provider && upd.ProviderId == providerId);

    return userProviderData?.User;
  }

  public async Task<User> CreateAsync(User user)
  {
    _dbContext.Users.Add(user);
    await _dbContext.SaveChangesAsync();
    return user;
  }

  public async Task<User> UpdateAsync(User user)
  {
    user.UpdatedAt = DateTime.UtcNow;
    _dbContext.Users.Update(user);
    await _dbContext.SaveChangesAsync();
    return user;
  }

  public async Task<bool> HasProviderLinkedAsync(string supabaseId, string provider)
  {
    return await _dbContext.Users
        .Where(u => u.SupabaseId == supabaseId)
        .SelectMany(u => u.ProviderData)
        .AnyAsync(pd => pd.Provider == provider);
  }

  public async Task<IEnumerable<string>> GetLinkedProvidersAsync(string supabaseId)
  {
    return await _dbContext.Users
        .Where(u => u.SupabaseId == supabaseId)
        .SelectMany(u => u.ProviderData)
        .Select(pd => pd.Provider)
        .ToListAsync();
  }

  public async Task SaveChangesAsync()
  {
    await _dbContext.SaveChangesAsync();
  }
}
