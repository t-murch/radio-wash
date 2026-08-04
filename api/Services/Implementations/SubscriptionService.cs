using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Exceptions;
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

  public async Task<UserSubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId)
  {
    return await _unitOfWork.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId);
  }

  public async Task<UserSubscription> SyncFromStripeAsync(
      Stripe.Subscription stripeSubscription,
      Func<Task<int?>>? resolveUserIdFallback = null)
  {
    if (!SubscriptionStatusMapper.TryMap(stripeSubscription.Status, out var status))
    {
      _logger.LogError(
        "Unknown Stripe subscription status {Status} for subscription {SubscriptionId}; storing raw value",
        stripeSubscription.Status, stripeSubscription.Id);
    }

    var (periodStart, periodEnd) = GetPeriodDates(stripeSubscription);

    var existing = await _unitOfWork.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscription.Id);
    if (existing != null)
    {
      return await UpdateExistingFromStripeAsync(existing, stripeSubscription, status, periodStart, periodEnd);
    }

    // No local row yet — either the `created` event, or a later event that arrived first.
    var priceId = stripeSubscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
    if (string.IsNullOrEmpty(priceId))
    {
      throw new InvalidOperationException(
        $"Stripe subscription {stripeSubscription.Id} has no items with a price; cannot resolve a local plan");
    }

    var plan = await GetPlanByStripePriceIdAsync(priceId)
      ?? throw new InvalidOperationException(
        $"No local plan matches Stripe price {priceId} for subscription {stripeSubscription.Id}; " +
        "check SubscriptionPlans seeding against the Stripe account");

    var userId = TryGetUserIdFromMetadata(stripeSubscription.Metadata);
    if (!userId.HasValue && resolveUserIdFallback != null)
    {
      userId = await resolveUserIdFallback();
    }
    if (!userId.HasValue)
    {
      throw new InvalidOperationException(
        $"Could not determine user for Stripe subscription {stripeSubscription.Id}: " +
        "no userId in subscription or customer metadata");
    }

    // The user has paid on Stripe's side, so a conflicting local subscription is an alert,
    // not a reason to drop the record.
    if (await HasActiveSubscriptionAsync(userId.Value))
    {
      _logger.LogError(
        "User {UserId} already has an active subscription but Stripe subscription {SubscriptionId} arrived; creating anyway",
        userId.Value, stripeSubscription.Id);
    }

    var created = await _unitOfWork.UserSubscriptions.TryCreateAsync(new UserSubscription
    {
      UserId = userId.Value,
      PlanId = plan.Id,
      StripeSubscriptionId = stripeSubscription.Id,
      StripeCustomerId = stripeSubscription.CustomerId,
      Status = status,
      CancelAtPeriodEnd = stripeSubscription.CancelAtPeriodEnd,
      CurrentPeriodStart = periodStart,
      CurrentPeriodEnd = periodEnd,
      CanceledAt = status == SubscriptionStatus.Canceled ? DateTime.UtcNow : null,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    });

    if (created == null)
    {
      // Lost the create race: the webhook, the checkout/complete endpoint, and the
      // reconciliation sweep can all try to materialize a brand-new subscription within
      // seconds of payment. The unique index arbitrates; the winner's committed row must be
      // readable now, so take the update path against it directly (bounded — no re-entry
      // into the create path, which could loop if the violation ever came from another
      // constraint).
      _logger.LogInformation(
        "Concurrent writer created the row for Stripe subscription {SubscriptionId} first; syncing instead",
        stripeSubscription.Id);

      var winner = await _unitOfWork.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscription.Id)
        ?? throw new InvalidOperationException(
          $"Insert for Stripe subscription {stripeSubscription.Id} hit a unique violation, " +
          "but no row with that StripeSubscriptionId exists — the violation came from a different constraint");

      return await UpdateExistingFromStripeAsync(winner, stripeSubscription, status, periodStart, periodEnd);
    }

    await ApplyStatusTransitionSideEffectsAsync(userId.Value, previousStatus: null, status);

    _logger.LogInformation(
      "Created subscription record for user {UserId} from Stripe subscription {SubscriptionId} with status {Status}",
      userId.Value, stripeSubscription.Id, status);
    return created;
  }

  private async Task<UserSubscription> UpdateExistingFromStripeAsync(
      UserSubscription existing,
      Stripe.Subscription stripeSubscription,
      string status,
      DateTime? periodStart,
      DateTime? periodEnd)
  {
    var previousStatus = existing.Status;

    existing.Status = status;
    existing.StripeCustomerId ??= stripeSubscription.CustomerId;
    // Stripe owns this flag: a portal-side "resume" clears it here automatically.
    existing.CancelAtPeriodEnd = stripeSubscription.CancelAtPeriodEnd;
    if (periodStart.HasValue && periodEnd.HasValue)
    {
      existing.CurrentPeriodStart = periodStart;
      existing.CurrentPeriodEnd = periodEnd;
    }
    if (status == SubscriptionStatus.Canceled && existing.CanceledAt == null)
    {
      existing.CanceledAt = DateTime.UtcNow;
    }

    var updated = await _unitOfWork.UserSubscriptions.UpdateAsync(existing);
    await ApplyStatusTransitionSideEffectsAsync(existing.UserId, previousStatus, status);

    _logger.LogInformation(
      "Synced subscription {SubscriptionId} from Stripe: {PreviousStatus} -> {Status}",
      stripeSubscription.Id, previousStatus, status);
    return updated;
  }

  private static (DateTime? Start, DateTime? End) GetPeriodDates(Stripe.Subscription stripeSubscription)
  {
    // Period dates live on subscription items (Stripe.net v49); multi-item subscriptions
    // use the latest period end.
    var items = stripeSubscription.Items?.Data;
    if (items == null || items.Count == 0)
    {
      return (null, null);
    }

    return (items.First().CurrentPeriodStart, items.Max(i => i.CurrentPeriodEnd));
  }

  private static int? TryGetUserIdFromMetadata(IDictionary<string, string>? metadata)
  {
    if (metadata != null
        && metadata.TryGetValue("userId", out var userIdStr)
        && int.TryParse(userIdStr, out var userId))
    {
      return userId;
    }

    return null;
  }

  private async Task ApplyStatusTransitionSideEffectsAsync(int userId, string? previousStatus, string newStatus)
  {
    if (SubscriptionStatusMapper.IsEntitled(newStatus) && !SubscriptionStatusMapper.IsEntitled(previousStatus))
    {
      await ReactivateSyncConfigsAsync(userId);
    }
    else if (SubscriptionStatusMapper.IsInactive(newStatus) && !SubscriptionStatusMapper.IsInactive(previousStatus))
    {
      await DisableSyncConfigsForUserAsync(userId);
    }
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

    if (!SubscriptionStatusMapper.TryMap(status, out var mappedStatus))
    {
      _logger.LogError(
        "Unknown subscription status {Status} for subscription {SubscriptionId}; storing raw value",
        status, stripeSubscriptionId);
    }

    _logger.LogInformation("Updating subscription {SubscriptionId} status from {OldStatus} to {NewStatus}",
        subscription.Id, subscription.Status, mappedStatus);

    var previousStatus = subscription.Status;
    subscription.Status = mappedStatus;
    if (mappedStatus == SubscriptionStatus.Canceled && subscription.CanceledAt == null)
    {
      subscription.CanceledAt = DateTime.UtcNow;
    }

    var updated = await _unitOfWork.UserSubscriptions.UpdateAsync(subscription);
    await ApplyStatusTransitionSideEffectsAsync(subscription.UserId, previousStatus, mappedStatus);
    return updated;
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

  public async Task<UserSubscription> MarkCancellationRequestedAsync(int userId)
  {
    var subscription = await _unitOfWork.UserSubscriptions.GetByUserIdAsync(userId);
    if (subscription == null)
    {
      throw new InvalidOperationException($"No subscription found for user {userId}");
    }

    _logger.LogInformation(
      "Marking subscription {SubscriptionId} for user {UserId} as cancel-at-period-end (active until {PeriodEnd})",
      subscription.Id, userId, subscription.CurrentPeriodEnd);

    // Deliberately no status change and no sync-config disabling: the user paid for the
    // rest of the period. customer.subscription.deleted performs the real deactivation.
    subscription.CancelAtPeriodEnd = true;

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

  public async Task EnforcePlanLimitAsync(int userId)
  {
    var subscription = await _unitOfWork.UserSubscriptions.GetByUserIdAsync(userId);
    if (subscription?.Plan == null)
    {
      return;
    }

    var maxPlaylists = subscription.Plan.MaxPlaylists;
    if (!maxPlaylists.HasValue)
    {
      return;
    }

    var current = await _unitOfWork.SyncConfigs.CountEnabledByUserIdAsync(userId);
    if (current >= maxPlaylists.Value)
    {
      throw new PlanLimitExceededException("playlists", maxPlaylists.Value, current);
    }
  }
}
