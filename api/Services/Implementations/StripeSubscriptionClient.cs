using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Services.Implementations;

public class StripeSubscriptionClient : IStripeSubscriptionClient
{
  private readonly StripeClient _stripeClient;

  public StripeSubscriptionClient(IConfiguration configuration)
  {
    _stripeClient = new StripeClient(configuration["Stripe:SecretKey"]);
  }

  public async Task<Subscription> CancelAtPeriodEndAsync(string stripeSubscriptionId)
  {
    var service = new Stripe.SubscriptionService(_stripeClient);
    var options = new SubscriptionUpdateOptions
    {
      CancelAtPeriodEnd = true
    };

    return await service.UpdateAsync(stripeSubscriptionId, options);
  }
}
