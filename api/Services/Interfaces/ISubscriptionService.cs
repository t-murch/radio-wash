using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

public interface ISubscriptionService
{
  Task<UserSubscription?> GetActiveSubscriptionAsync(int userId);
  Task<bool> HasActiveSubscriptionAsync(int userId);
  Task<UserSubscription> CreateSubscriptionAsync(int userId, int planId, string stripeSubscriptionId, string stripeCustomerId);
  Task<UserSubscription> UpdateSubscriptionStatusAsync(string stripeSubscriptionId, string status);
  Task<UserSubscription> UpdateSubscriptionDatesAsync(string stripeSubscriptionId, DateTime currentPeriodStart, DateTime currentPeriodEnd);
  Task<UserSubscription> CancelSubscriptionAsync(int userId);
  Task<IEnumerable<SubscriptionPlan>> GetAvailablePlansAsync();
  Task<SubscriptionPlan?> GetPlanByIdAsync(int planId);
  Task<SubscriptionPlan?> GetPlanByStripePriceIdAsync(string stripePriceId);
  Task ValidateSubscriptionsAsync(); // For periodic validation
}
