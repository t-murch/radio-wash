using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface IUserSubscriptionRepository
{
  Task<UserSubscription?> GetByIdAsync(int subscriptionId);
  Task<UserSubscription?> GetByUserIdAsync(int userId);
  Task<UserSubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId);
  Task<IEnumerable<UserSubscription>> GetActiveSubscriptionsAsync();
  Task<IEnumerable<UserSubscription>> GetExpiringSubscriptionsAsync(DateTime before);
  Task<UserSubscription> CreateAsync(UserSubscription subscription);
  Task<UserSubscription> UpdateAsync(UserSubscription subscription);
  Task<bool> HasActiveSubscriptionAsync(int userId);
  Task SaveChangesAsync();
}
