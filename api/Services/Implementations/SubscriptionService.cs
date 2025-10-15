using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class SubscriptionService : ISubscriptionService
{
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

    // Disable all sync configs for this user
    var syncConfigs = await _unitOfWork.SyncConfigs.GetByUserIdAsync(userId);
    foreach (var config in syncConfigs)
    {
      await _unitOfWork.SyncConfigs.DisableConfigAsync(config.Id);
    }

    return await _unitOfWork.UserSubscriptions.UpdateAsync(subscription);
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
    _logger.LogInformation("Starting subscription validation");

    var expiredSubscriptions = await _unitOfWork.UserSubscriptions.GetExpiringSubscriptionsAsync(DateTime.UtcNow);

    foreach (var subscription in expiredSubscriptions)
    {
      _logger.LogWarning("Subscription {SubscriptionId} for user {UserId} has expired",
          subscription.Id, subscription.UserId);

      // Disable sync configs for expired subscriptions
      var syncConfigs = await _unitOfWork.SyncConfigs.GetByUserIdAsync(subscription.UserId);
      foreach (var config in syncConfigs)
      {
        await _unitOfWork.SyncConfigs.DisableConfigAsync(config.Id);
      }
    }

    _logger.LogInformation("Subscription validation completed. {ExpiredCount} subscriptions processed",
        expiredSubscriptions.Count());
  }
}
