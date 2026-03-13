using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace RadioWash.Api.Services.Implementations;

public class StripePaymentService : IPaymentService
{
  private readonly IConfiguration _configuration;
  private readonly ISubscriptionService _subscriptionService;
  private readonly RadioWashDbContext _dbContext;
  private readonly CustomerService _customerService;
  private readonly ILogger<StripePaymentService> _logger;
  private readonly StripeClient _stripeClient;

  public StripePaymentService(
      IConfiguration configuration,
      ISubscriptionService subscriptionService,
      RadioWashDbContext dbContext,
      CustomerService customerService,
      ILogger<StripePaymentService> logger,
      StripeClient stripeClient)
  {
    _configuration = configuration;
    _subscriptionService = subscriptionService;
    _dbContext = dbContext;
    _customerService = customerService;
    _logger = logger;
    _stripeClient = stripeClient;
  }

  public async Task<string> CreateCheckoutSessionAsync(int userId, int planId)
  {
    // Server-side price lookup — never trust client-provided price IDs
    var plan = await _subscriptionService.GetPlanByIdAsync(planId);
    if (plan == null)
    {
      throw new InvalidOperationException($"Subscription plan {planId} not found");
    }

    if (!plan.IsActive)
    {
      throw new InvalidOperationException($"Subscription plan {planId} is not active");
    }

    if (string.IsNullOrEmpty(plan.StripePriceId))
    {
      throw new InvalidOperationException($"Subscription plan {planId} has no Stripe price configured");
    }

    var options = new SessionCreateOptions
    {
      PaymentMethodTypes = new List<string> { "card" },
      LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = plan.StripePriceId,
                    Quantity = 1
                }
            },
      Mode = "subscription",
      SuccessUrl = $"{_configuration["FrontendUrl"]}/subscription/success?session_id={{CHECKOUT_SESSION_ID}}",
      CancelUrl = $"{_configuration["FrontendUrl"]}/subscription/cancel",
      Metadata = new Dictionary<string, string>
            {
                { "userId", userId.ToString() }
            },
      SubscriptionData = new SessionSubscriptionDataOptions
      {
        Metadata = new Dictionary<string, string>
        {
          { "userId", userId.ToString() }
        }
      }
    };

    var service = new SessionService(_stripeClient);
    var session = await service.CreateAsync(options);

    _logger.LogInformation("Created Stripe checkout session {SessionId} for user {UserId} with plan {PlanId}", session.Id, userId, planId);

    return session.Url;
  }

  public async Task<string> CreatePortalSessionAsync(string customerId)
  {
    var options = new Stripe.BillingPortal.SessionCreateOptions
    {
      Customer = customerId,
      ReturnUrl = $"{_configuration["FrontendUrl"]}/dashboard"
    };

    var service = new Stripe.BillingPortal.SessionService(_stripeClient);
    var session = await service.CreateAsync(options);

    return session.Url;
  }

  public async Task<Stripe.Checkout.Session?> VerifyCheckoutSessionAsync(string sessionId)
  {
    try
    {
      var service = new SessionService(_stripeClient);
      var session = await service.GetAsync(sessionId);

      if (session.Status == "complete")
      {
        _logger.LogInformation("Checkout session {SessionId} verified as complete", sessionId);
        return session;
      }

      _logger.LogInformation("Checkout session {SessionId} has status {Status}, not complete", sessionId, session.Status);
      return null;
    }
    catch (StripeException ex)
    {
      _logger.LogWarning(ex, "Failed to verify checkout session {SessionId}", sessionId);
      return null;
    }
  }

}
