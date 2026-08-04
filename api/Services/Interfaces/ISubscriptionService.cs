using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

public interface ISubscriptionService
{
  Task<UserSubscription?> GetActiveSubscriptionAsync(int userId);
  Task<bool> HasActiveSubscriptionAsync(int userId);
  Task<UserSubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId);
  Task<UserSubscription> CreateSubscriptionAsync(int userId, int planId, string stripeSubscriptionId, string stripeCustomerId);
  Task<UserSubscription> UpdateSubscriptionStatusAsync(string stripeSubscriptionId, string status);
  // Creates or updates the local subscription row from Stripe's current view of the
  // subscription — the single write path for webhook events, checkout reconciliation, and
  // the reconciliation job. Keyed on StripeSubscriptionId, so event ordering doesn't
  // matter: an `updated` arriving before `created` simply creates the row. Status goes
  // through SubscriptionStatusMapper and transition side effects (disabling or
  // re-enabling sync configs) are applied here. resolveUserIdFallback is invoked only
  // when a row must be created and the subscription's metadata carries no userId
  // (e.g. a Stripe API lookup of the customer's metadata).
  Task<UserSubscription> SyncFromStripeAsync(Stripe.Subscription stripeSubscription, Func<Task<int?>>? resolveUserIdFallback = null);
  Task<UserSubscription> UpdateSubscriptionDatesAsync(string stripeSubscriptionId, DateTime currentPeriodStart, DateTime currentPeriodEnd);
  // Marks the local subscription cancel-at-period-end after the Stripe-side cancellation
  // succeeded. Does NOT change status or disable sync configs — access continues until the
  // customer.subscription.deleted webhook arrives at period end.
  Task<UserSubscription> MarkCancellationRequestedAsync(int userId);
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
  // Verifies the user's enabled sync-config count is strictly less than their plan's
  // MaxPlaylists before creating another one. Throws PlanLimitExceededException when at
  // or above the limit. A null MaxPlaylists means unlimited and is always allowed. No
  // active subscription leaves this method as a no-op; callers gate that separately with
  // HasActiveSubscriptionAsync so the two error paths stay distinct at the HTTP boundary.
  Task EnforcePlanLimitAsync(int userId);
}
