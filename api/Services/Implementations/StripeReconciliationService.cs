using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Services.Implementations;

public class StripeReconciliationService : IStripeReconciliationService
{
  private readonly IConfiguration _configuration;
  private readonly ISubscriptionService _subscriptionService;
  private readonly Stripe.SubscriptionService _stripeSubscriptionService;
  private readonly CustomerService _customerService;
  private readonly ILogger<StripeReconciliationService> _logger;

  public StripeReconciliationService(
      IConfiguration configuration,
      ISubscriptionService subscriptionService,
      Stripe.SubscriptionService stripeSubscriptionService,
      CustomerService customerService,
      ILogger<StripeReconciliationService> logger)
  {
    _configuration = configuration;
    _subscriptionService = subscriptionService;
    _stripeSubscriptionService = stripeSubscriptionService;
    _customerService = customerService;
    _logger = logger;

    StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
  }

  public async Task<StripeReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default)
  {
    var result = new StripeReconciliationResult();

    await ReconcileLocalSubscriptionsAsync(result, cancellationToken);
    await ReconcileMissingLocalRowsAsync(result, cancellationToken);

    _logger.LogInformation(
      "Stripe reconciliation finished: {LocalChecked} local checked, {LocalUpdated} updated, {MissingCreated} missing rows created, {Errors} errors",
      result.LocalChecked, result.LocalUpdated, result.MissingCreated, result.Errors);

    return result;
  }

  // Pass 1: every non-terminal local subscription is re-read from Stripe and upserted, so a
  // lost status/date/cancel-flag webhook heals within one interval.
  private async Task ReconcileLocalSubscriptionsAsync(StripeReconciliationResult result, CancellationToken cancellationToken)
  {
    var localSubscriptions = (await _subscriptionService.GetReconcilableSubscriptionsAsync()).ToList();

    // A user with more than one entitled subscription is being double-billed. Logged as an
    // error on EVERY sweep (not just at creation time) so the alert repeats until a human
    // resolves it — see the runbook's duplicate-active-subscription entry.
    foreach (var duplicates in localSubscriptions
      .Where(s => SubscriptionStatusMapper.IsEntitled(s.Status))
      .GroupBy(s => s.UserId)
      .Where(g => g.Count() > 1))
    {
      _logger.LogError(
        "User {UserId} has {Count} entitled subscriptions ({SubscriptionIds}) — double billing; cancel the older one in the Stripe Dashboard",
        duplicates.Key, duplicates.Count(), string.Join(", ", duplicates.Select(s => s.StripeSubscriptionId)));
    }

    foreach (var local in localSubscriptions)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (string.IsNullOrEmpty(local.StripeSubscriptionId))
      {
        continue;
      }

      result.LocalChecked++;

      try
      {
        var stripeSubscription = await _stripeSubscriptionService.GetAsync(
          local.StripeSubscriptionId, cancellationToken: cancellationToken);

        await _subscriptionService.SyncFromStripeAsync(stripeSubscription);
        result.LocalUpdated++;
      }
      catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
      {
        // Stripe no longer knows this subscription — the deletion webhook was lost.
        _logger.LogWarning(
          "Local subscription {SubscriptionId} (Stripe {StripeSubscriptionId}) no longer exists on Stripe; canceling locally",
          local.Id, local.StripeSubscriptionId);

        try
        {
          await _subscriptionService.UpdateSubscriptionStatusAsync(local.StripeSubscriptionId, SubscriptionStatus.Canceled);
          result.LocalUpdated++;
        }
        catch (Exception cancelEx)
        {
          result.Errors++;
          _logger.LogError(cancelEx, "Failed to cancel orphaned local subscription {SubscriptionId}", local.Id);
        }
      }
      catch (Exception ex)
      {
        result.Errors++;
        _logger.LogError(ex, "Failed to reconcile local subscription {SubscriptionId} (Stripe {StripeSubscriptionId})",
          local.Id, local.StripeSubscriptionId);
      }
    }
  }

  // Pass 2: every entitled subscription on Stripe must have a matching local row. This is
  // the recovery path for "user charged, all webhooks lost". Stripe's list filter takes a
  // single status, so each entitled status gets its own listing pass — the set here must
  // stay in lockstep with SubscriptionStatusMapper.IsEntitled, or subscriptions in the
  // missing status become invisible to the sweep (unhealable wrongful cancellations,
  // uncreatable missing rows).
  private static readonly string[] EntitledStripeStatuses = { "active", "trialing" };

  private async Task ReconcileMissingLocalRowsAsync(StripeReconciliationResult result, CancellationToken cancellationToken)
  {
    foreach (var entitledStatus in EntitledStripeStatuses)
    {
      await ReconcileMissingLocalRowsForStatusAsync(entitledStatus, result, cancellationToken);
    }
  }

  private async Task ReconcileMissingLocalRowsForStatusAsync(
      string stripeStatus, StripeReconciliationResult result, CancellationToken cancellationToken)
  {
    var options = new SubscriptionListOptions { Status = stripeStatus, Limit = 100 };

    await foreach (var stripeSubscription in _stripeSubscriptionService
      .ListAutoPagingAsync(options, cancellationToken: cancellationToken))
    {
      try
      {
        var local = await _subscriptionService.GetByStripeSubscriptionIdAsync(stripeSubscription.Id);
        if (local != null)
        {
          // Stripe says active but the local row is not entitled — e.g. the expiry sweep
          // canceled a paying user while Stripe was unreachable. Pass 1 skips canceled rows,
          // so without this branch a wrongly-canceled subscription would never heal.
          if (!SubscriptionStatusMapper.IsEntitled(local.Status))
          {
            _logger.LogWarning(
              "Stripe subscription {StripeSubscriptionId} is {StripeStatus} but local status is {LocalStatus}; healing from Stripe state",
              stripeSubscription.Id, stripeStatus, local.Status);

            await _subscriptionService.SyncFromStripeAsync(
              stripeSubscription,
              () => ResolveUserIdFromCustomerAsync(stripeSubscription.CustomerId));
            result.LocalUpdated++;
          }

          continue;
        }

        _logger.LogWarning(
          "Stripe subscription {StripeSubscriptionId} is {StripeStatus} but has no local record; creating from Stripe state",
          stripeSubscription.Id, stripeStatus);

        await _subscriptionService.SyncFromStripeAsync(
          stripeSubscription,
          () => ResolveUserIdFromCustomerAsync(stripeSubscription.CustomerId));

        result.MissingCreated++;
      }
      catch (Exception ex)
      {
        result.Errors++;
        _logger.LogError(ex,
          "Failed to reconcile Stripe subscription {StripeSubscriptionId} with no local record",
          stripeSubscription.Id);
      }
    }
  }

  private async Task<int?> ResolveUserIdFromCustomerAsync(string customerId)
  {
    var customer = await _customerService.GetAsync(customerId);
    if (customer?.Metadata?.TryGetValue("userId", out var userIdStr) == true
        && int.TryParse(userIdStr, out var userId))
    {
      return userId;
    }

    return null;
  }
}
