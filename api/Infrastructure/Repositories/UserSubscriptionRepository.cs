using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class UserSubscriptionRepository : IUserSubscriptionRepository
{
  private readonly RadioWashDbContext _dbContext;

  public UserSubscriptionRepository(RadioWashDbContext dbContext)
  {
    _dbContext = dbContext;
  }

  public async Task<UserSubscription?> GetByIdAsync(int subscriptionId)
  {
    return await _dbContext.UserSubscriptions
        .AsNoTracking()
        .FirstOrDefaultAsync(us => us.Id == subscriptionId);
  }

  public async Task<UserSubscription?> GetByIdWithDetailsAsync(int subscriptionId)
  {
    return await _dbContext.UserSubscriptions
        .Include(us => us.User)
        .Include(us => us.Plan)
        .FirstOrDefaultAsync(us => us.Id == subscriptionId);
  }

  public async Task<UserSubscription?> GetByUserIdAsync(int userId)
  {
    return await _dbContext.UserSubscriptions
        .Include(us => us.Plan)
        .Where(us => us.UserId == userId)
        .OrderByDescending(us => us.CreatedAt)
        .FirstOrDefaultAsync();
  }

  public async Task<UserSubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId)
  {
    return await _dbContext.UserSubscriptions
        .FirstOrDefaultAsync(us => us.StripeSubscriptionId == stripeSubscriptionId);
  }

  public async Task<UserSubscription?> GetByStripeSubscriptionIdWithDetailsAsync(string stripeSubscriptionId)
  {
    return await _dbContext.UserSubscriptions
        .Include(us => us.User)
        .Include(us => us.Plan)
        .FirstOrDefaultAsync(us => us.StripeSubscriptionId == stripeSubscriptionId);
  }

  public async Task<IEnumerable<UserSubscription>> GetActiveSubscriptionsAsync()
  {
    return await _dbContext.UserSubscriptions
        .AsNoTracking()
        .Where(us => us.Status == SubscriptionStatus.Active)
        .ToListAsync();
  }

  public async Task<IEnumerable<UserSubscription>> GetActiveSubscriptionsWithDetailsAsync()
  {
    return await _dbContext.UserSubscriptions
        .Include(us => us.User)
        .Include(us => us.Plan)
        .Where(us => us.Status == SubscriptionStatus.Active)
        .ToListAsync();
  }

  public async Task<IEnumerable<UserSubscription>> GetExpiringSubscriptionsAsync(DateTime before)
  {
    return await _dbContext.UserSubscriptions
        .AsNoTracking()
        .Where(us => us.Status == SubscriptionStatus.Active &&
                    us.CurrentPeriodEnd.HasValue &&
                    us.CurrentPeriodEnd.Value <= before)
        .ToListAsync();
  }

  public async Task<IEnumerable<UserSubscription>> GetExpiringSubscriptionsWithDetailsAsync(DateTime before)
  {
    return await _dbContext.UserSubscriptions
        .Include(us => us.User)
        .Include(us => us.Plan)
        .Where(us => us.Status == SubscriptionStatus.Active &&
                    us.CurrentPeriodEnd.HasValue &&
                    us.CurrentPeriodEnd.Value <= before)
        .ToListAsync();
  }

  public async Task<UserSubscription> CreateAsync(UserSubscription subscription)
  {
    _dbContext.UserSubscriptions.Add(subscription);
    await _dbContext.SaveChangesAsync();
    return subscription;
  }

  public async Task<UserSubscription> UpdateAsync(UserSubscription subscription)
  {
    subscription.UpdatedAt = DateTime.UtcNow;
    _dbContext.UserSubscriptions.Update(subscription);
    await _dbContext.SaveChangesAsync();
    return subscription;
  }

  public async Task<bool> HasActiveSubscriptionAsync(int userId)
  {
    return await _dbContext.UserSubscriptions
        .AnyAsync(us => us.UserId == userId &&
                       us.Status == SubscriptionStatus.Active &&
                       (us.CurrentPeriodEnd == null || us.CurrentPeriodEnd > DateTime.UtcNow));
  }

  public async Task SaveChangesAsync()
  {
    await _dbContext.SaveChangesAsync();
  }
}
