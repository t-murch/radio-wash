namespace RadioWash.Api.Services.Interfaces;

public class StripeReconciliationResult
{
  public int LocalChecked { get; set; }
  public int LocalUpdated { get; set; }
  public int MissingCreated { get; set; }
  public int Errors { get; set; }
}

/// <summary>
/// Safety net for webhook loss: periodically reconciles local subscription state against
/// Stripe in both directions. Catches the "user charged on Stripe but no local row" case
/// (all webhooks lost) and local rows that drifted from Stripe's status.
/// </summary>
public interface IStripeReconciliationService
{
  Task<StripeReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default);
}
