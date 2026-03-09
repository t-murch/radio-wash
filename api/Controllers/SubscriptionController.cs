using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubscriptionController : AuthenticatedControllerBase
{
  private readonly ISubscriptionService _subscriptionService;
  private readonly IPaymentService _paymentService;
  private readonly ILogger<SubscriptionController> _logger;

  public SubscriptionController(
      RadioWashDbContext dbContext,
      ISubscriptionService subscriptionService,
      IPaymentService paymentService,
      ILogger<SubscriptionController> logger) : base(dbContext, logger)
  {
    _subscriptionService = subscriptionService;
    _paymentService = paymentService;
    _logger = logger;
  }

  [HttpGet("plans")]
  public async Task<ActionResult<IEnumerable<SubscriptionPlanDto>>> GetAvailablePlans()
  {
    var plans = await _subscriptionService.GetAvailablePlansAsync();
    var planDtos = plans.Select(p => p.ToDto());
    return Ok(planDtos);
  }

  [HttpGet("current")]
  public async Task<ActionResult<UserSubscriptionDto?>> GetCurrentSubscription()
  {
    var userId = GetCurrentUserId();
    var subscription = await _subscriptionService.GetActiveSubscriptionAsync(userId);

    if (subscription == null)
    {
      return Ok(null);
    }

    return Ok(subscription.ToDto());
  }

  [HttpPost("checkout")]
  public async Task<ActionResult> CreateCheckoutSession([FromBody] CreateCheckoutDto dto)
  {
    var userId = GetCurrentUserId();

    try
    {
      var checkoutUrl = await _paymentService.CreateCheckoutSessionAsync(userId, dto.PlanId);
      return Ok(new { checkoutUrl });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create checkout session for user {UserId}", userId);
      return Problem(
        detail: "Failed to create checkout session",
        statusCode: StatusCodes.Status400BadRequest
      );
    }
  }

  [HttpPost("portal")]
  public async Task<ActionResult> CreatePortalSession()
  {
    var userId = GetCurrentUserId();
    var subscription = await _subscriptionService.GetActiveSubscriptionAsync(userId);

    if (subscription?.StripeCustomerId == null)
    {
      return Problem(
        detail: "No active subscription found",
        statusCode: StatusCodes.Status400BadRequest
      );
    }

    try
    {
      var portalUrl = await _paymentService.CreatePortalSessionAsync(subscription.StripeCustomerId);
      return Ok(new { portalUrl });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create portal session for user {UserId}", userId);
      return Problem(
        detail: "Failed to create portal session",
        statusCode: StatusCodes.Status400BadRequest
      );
    }
  }

  [HttpPost("cancel")]
  public async Task<ActionResult> CancelSubscription()
  {
    var userId = GetCurrentUserId();

    try
    {
      await _subscriptionService.CancelSubscriptionAsync(userId);
      return Ok(new { message = "Subscription canceled successfully" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to cancel subscription for user {UserId}", userId);
      return BadRequest(new { error = "Failed to cancel subscription" });
    }
  }

  [HttpPost("webhook")]
  [AllowAnonymous]
  public async Task<ActionResult> HandleStripeWebhook()
  {
    try
    {
      var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
      var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();

      if (string.IsNullOrEmpty(signature))
      {
        return BadRequest("Missing Stripe signature");
      }

      await _paymentService.HandleWebhookAsync(json, signature);
      return Ok();
    }
    catch (Stripe.StripeException ex)
    {
      _logger.LogError(ex, "Stripe webhook signature verification failed");
      return BadRequest("Invalid webhook signature");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing Stripe webhook");
      return Ok(); // Return OK to prevent Stripe from retrying - we handle retries internally
    }
  }

  [HttpGet("status")]
  public async Task<ActionResult> GetSubscriptionStatus()
  {
    var userId = GetCurrentUserId();
    var hasActiveSubscription = await _subscriptionService.HasActiveSubscriptionAsync(userId);

    return Ok(new { hasActiveSubscription });
  }
}
