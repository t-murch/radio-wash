namespace RadioWash.Api.Services.Interfaces;

public interface IStripeSubscriptionClient
{
  Task<Stripe.Subscription> CancelAtPeriodEndAsync(string stripeSubscriptionId);
}
