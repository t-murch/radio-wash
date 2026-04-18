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
  // Runs on a periodic background tick. Transitions Active → Canceled for subscriptions whose
  // CurrentPeriodEnd is beyond the grace window, and disables enabled sync configs belonging
  // to those users with AutoDisabledReason = SubscriptionInactive.
  Task ValidateSubscriptionsAsync();
  // Re-enables any sync configs that were previously auto-disabled due to subscription
  // inactivity. Called by the webhook processor when a user's subscription transitions back
  // to Active so their syncs resume without manual intervention.
  Task ReactivateSyncConfigsAsync(int userId);
}
