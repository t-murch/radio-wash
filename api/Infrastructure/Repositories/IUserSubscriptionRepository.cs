using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface IUserSubscriptionRepository
{
  Task<UserSubscription?> GetByIdAsync(int subscriptionId);
  Task<UserSubscription?> GetByIdWithDetailsAsync(int subscriptionId);
  Task<UserSubscription?> GetByUserIdAsync(int userId);
  Task<UserSubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId);
  Task<UserSubscription?> GetByStripeSubscriptionIdWithDetailsAsync(string stripeSubscriptionId);
  Task<IEnumerable<UserSubscription>> GetActiveSubscriptionsAsync();
  Task<IEnumerable<UserSubscription>> GetActiveSubscriptionsWithDetailsAsync();
  Task<IEnumerable<UserSubscription>> GetExpiringSubscriptionsAsync(DateTime before);
  Task<IEnumerable<UserSubscription>> GetExpiringSubscriptionsWithDetailsAsync(DateTime before);
  Task<IEnumerable<UserSubscription>> GetExpiredActiveSubscriptionsAsync(DateTime cutoff);
  // Subscriptions worth re-checking against Stripe: everything with a Stripe id that isn't
  // terminally canceled. Used by the reconciliation sweep.
  Task<IEnumerable<UserSubscription>> GetReconcilableSubscriptionsAsync();
  Task<UserSubscription> CreateAsync(UserSubscription subscription);
  // Returns null when a concurrent writer already created a row for the same
  // StripeSubscriptionId (unique filtered index); the failed insert is detached so the
  // context remains usable for a follow-up read/update.
  Task<UserSubscription?> TryCreateAsync(UserSubscription subscription);
  Task<UserSubscription> UpdateAsync(UserSubscription subscription);
  Task<bool> HasActiveSubscriptionAsync(int userId);
  Task SaveChangesAsync();
}
