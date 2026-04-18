using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class SubscriptionService : ISubscriptionService
{
  // Renewal webhooks from Stripe can land minutes after CurrentPeriodEnd, so the expiry job
  // only transitions subscriptions that are past the end of their period by this window.
  private static readonly TimeSpan ExpiryGraceWindow = TimeSpan.FromHours(24);

  private readonly IUnitOfWork _unitOfWork;
  private readonly ILogger<SubscriptionService> _logger;

  public SubscriptionService(
      IUnitOfWork unitOfWork,
      ILogger<SubscriptionService> logger)
  {
    _unitOfWork = unitOfWork;
    _logger = logger;
  }

  public async Task<UserSubscription?> GetActiveSubscriptionAsync(int userId)
  {
    return await _unitOfWork.UserSubscriptions.GetByUserIdAsync(userId);
  }

  public async Task<bool> HasActiveSubscriptionAsync(int userId)
  {
    return await _unitOfWork.UserSubscriptions.HasActiveSubscriptionAsync(userId);
  }

  public async Task<UserSubscription> CreateSubscriptionAsync(int userId, int planId, string stripeSubscriptionId, string stripeCustomerId)
  {
    _logger.LogInformation("Creating subscription for user {UserId} with plan {PlanId}", userId, planId);

    // Validate that user doesn't already have an active subscription
    var hasActiveSubscription = await HasActiveSubscriptionAsync(userId);
    if (hasActiveSubscription)
    {
      _logger.LogError("Cannot create subscription for user {UserId}: user already has an active subscription", userId);
      throw new InvalidOperationException($"User {userId} already has an active subscription");
    }

    var subscription = new UserSubscription
    {
      UserId = userId,
      PlanId = planId,
      StripeSubscriptionId = stripeSubscriptionId,
      StripeCustomerId = stripeCustomerId,
      Status = SubscriptionStatus.Active,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    return await _unitOfWork.UserSubscriptions.CreateAsync(subscription);
  }

  public async Task<UserSubscription> UpdateSubscriptionStatusAsync(string stripeSubscriptionId, string status)
  {
    var subscription = await _unitOfWork.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId);
    if (subscription == null)
    {
      throw new InvalidOperationException($"Subscription with Stripe ID {stripeSubscriptionId} not found");
    }

    _logger.LogInformation("Updating subscription {SubscriptionId} status from {OldStatus} to {NewStatus}",
        subscription.Id, subscription.Status, status);

    subscription.Status = status;
    if (status == SubscriptionStatus.Canceled)
    {
      subscription.CanceledAt = DateTime.UtcNow;
    }

    return await _unitOfWork.UserSubscriptions.UpdateAsync(subscription);
  }

  public async Task<UserSubscription> UpdateSubscriptionDatesAsync(string stripeSubscriptionId, DateTime currentPeriodStart, DateTime currentPeriodEnd)
  {
    var subscription = await _unitOfWork.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId);
    if (subscription == null)
    {
      throw new InvalidOperationException($"Subscription with Stripe ID {stripeSubscriptionId} not found");
    }

    subscription.CurrentPeriodStart = currentPeriodStart;
    subscription.CurrentPeriodEnd = currentPeriodEnd;

    return await _unitOfWork.UserSubscriptions.UpdateAsync(subscription);
  }

  public async Task<UserSubscription> CancelSubscriptionAsync(int userId)
  {
    var subscription = await _unitOfWork.UserSubscriptions.GetByUserIdAsync(userId);
    if (subscription == null)
    {
      throw new InvalidOperationException($"No active subscription found for user {userId}");
    }

    _logger.LogInformation("Canceling subscription {SubscriptionId} for user {UserId}", subscription.Id, userId);

    subscription.Status = SubscriptionStatus.Canceled;
    subscription.CanceledAt = DateTime.UtcNow;

    await DisableSyncConfigsForUserAsync(userId);

    return await _unitOfWork.UserSubscriptions.UpdateAsync(subscription);
  }

  private async Task DisableSyncConfigsForUserAsync(int userId)
  {
    var enabledConfigs = await _unitOfWork.SyncConfigs.GetEnabledByUserIdAsync(userId);
    foreach (var config in enabledConfigs)
    {
      await _unitOfWork.SyncConfigs.DisableConfigAsync(config.Id, AutoDisableReason.SubscriptionInactive);
    }
  }

  public async Task<IEnumerable<SubscriptionPlan>> GetAvailablePlansAsync()
  {
    return await _unitOfWork.SubscriptionPlans.GetActiveAsync();
  }

  public async Task<SubscriptionPlan?> GetPlanByIdAsync(int planId)
  {
    return await _unitOfWork.SubscriptionPlans.GetByIdAsync(planId);
  }

  public async Task<SubscriptionPlan?> GetPlanByStripePriceIdAsync(string stripePriceId)
  {
    return await _unitOfWork.SubscriptionPlans.GetByStripePriceIdAsync(stripePriceId);
  }

  public async Task ValidateSubscriptionsAsync()
  {
    var cutoff = DateTime.UtcNow - ExpiryGraceWindow;
    var expiredSubscriptions = await _unitOfWork.UserSubscriptions.GetExpiredActiveSubscriptionsAsync(cutoff);
    var expiredList = expiredSubscriptions.ToList();

    if (expiredList.Count == 0)
    {
      return;
    }

    _logger.LogInformation("Expiring {Count} subscriptions past the grace window", expiredList.Count);

    foreach (var subscription in expiredList)
    {
      try
      {
        subscription.Status = SubscriptionStatus.Canceled;
        subscription.CanceledAt = DateTime.UtcNow;
        await _unitOfWork.UserSubscriptions.UpdateAsync(subscription);

        await DisableSyncConfigsForUserAsync(subscription.UserId);

        _logger.LogInformation(
          "Expired subscription {SubscriptionId} for user {UserId}; CurrentPeriodEnd was {PeriodEnd}",
          subscription.Id, subscription.UserId, subscription.CurrentPeriodEnd);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex,
          "Failed to expire subscription {SubscriptionId} for user {UserId}; continuing batch",
          subscription.Id, subscription.UserId);
      }
    }
  }

  public async Task ReactivateSyncConfigsAsync(int userId)
  {
    var autoDisabled = await _unitOfWork.SyncConfigs.GetAutoDisabledByUserIdAsync(
      userId, AutoDisableReason.SubscriptionInactive);
    var toReenable = autoDisabled.ToList();

    if (toReenable.Count == 0)
    {
      return;
    }

    foreach (var config in toReenable)
    {
      await _unitOfWork.SyncConfigs.EnableConfigAsync(config.Id);
    }

    _logger.LogInformation(
      "Re-enabled {Count} sync configs for user {UserId} after subscription reactivation",
      toReenable.Count, userId);
  }
}
