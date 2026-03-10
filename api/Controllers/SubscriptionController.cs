using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;
using System.Text.Json;
using Stripe;

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

    var planDtos = plans.Select(p => new SubscriptionPlanDto
    {
      Id = p.Id,
      Name = p.Name,
      Price = p.PriceInCents / 100m,
      BillingPeriod = p.BillingPeriod,
      StripePriceId = p.StripePriceId,
      MaxPlaylists = p.MaxPlaylists,
      MaxTracksPerPlaylist = p.MaxTracksPerPlaylist,
      Features = ParseFeatures(p.Features),
      IsActive = p.IsActive
    });

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

    if (subscription.Plan == null)
    {
      _logger.LogWarning("Subscription {SubscriptionId} for user {UserId} has no associated plan",
          subscription.Id, userId);
      return Problem(
          title: "Subscription Data Incomplete",
          detail: "The subscription has no associated plan",
          statusCode: StatusCodes.Status500InternalServerError);
    }

    var subscriptionDto = new UserSubscriptionDto
    {
      Id = subscription.Id,
      Status = subscription.Status,
      CurrentPeriodStart = subscription.CurrentPeriodStart,
      CurrentPeriodEnd = subscription.CurrentPeriodEnd,
      CanceledAt = subscription.CanceledAt,
      Plan = new SubscriptionPlanDto
      {
        Id = subscription.Plan.Id,
        Name = subscription.Plan.Name,
        Price = subscription.Plan.PriceInCents / 100m,
        BillingPeriod = subscription.Plan.BillingPeriod,
        StripePriceId = subscription.Plan.StripePriceId,
        MaxPlaylists = subscription.Plan.MaxPlaylists,
        MaxTracksPerPlaylist = subscription.Plan.MaxTracksPerPlaylist,
        Features = ParseFeatures(subscription.Plan.Features),
        IsActive = subscription.Plan.IsActive
      },
      CreatedAt = subscription.CreatedAt
    };

    return Ok(subscriptionDto);
  }

  [HttpPost("checkout")]
  public async Task<ActionResult> CreateCheckoutSession([FromBody] CreateCheckoutDto dto)
  {
    var userId = GetCurrentUserId();

    try
    {
      var checkoutUrl = await _paymentService.CreateCheckoutSessionAsync(userId, dto.PlanPriceId);
      return Ok(new { checkoutUrl });
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogWarning(ex, "Invalid checkout session request for user {UserId}", userId);
      return BadRequest(new { error = ex.Message });
    }
    catch (StripeException ex)
    {
      _logger.LogError(ex, "Stripe error creating checkout session for user {UserId}", userId);
      return BadRequest(new { error = "Payment provider error. Please try again." });
    }
  }

  [HttpPost("portal")]
  public async Task<ActionResult> CreatePortalSession()
  {
    var userId = GetCurrentUserId();
    var subscription = await _subscriptionService.GetActiveSubscriptionAsync(userId);

    if (subscription?.StripeCustomerId == null)
    {
      return BadRequest(new { error = "No active subscription found" });
    }

    try
    {
      var portalUrl = await _paymentService.CreatePortalSessionAsync(subscription.StripeCustomerId);
      return Ok(new { portalUrl });
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogWarning(ex, "Invalid portal session request for user {UserId}", userId);
      return BadRequest(new { error = ex.Message });
    }
    catch (StripeException ex)
    {
      _logger.LogError(ex, "Stripe error creating portal session for user {UserId}", userId);
      return BadRequest(new { error = "Payment provider error. Please try again." });
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
    catch (InvalidOperationException ex)
    {
      _logger.LogWarning(ex, "Invalid cancel subscription request for user {UserId}", userId);
      return BadRequest(new { error = ex.Message });
    }
    catch (StripeException ex)
    {
      _logger.LogError(ex, "Stripe error cancelling subscription for user {UserId}", userId);
      return BadRequest(new { error = "Payment provider error. Please try again." });
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
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing Stripe webhook");
      return BadRequest("Webhook processing failed");
    }
  }

  [HttpGet("status")]
  public async Task<ActionResult> GetSubscriptionStatus()
  {
    var userId = GetCurrentUserId();
    var hasActiveSubscription = await _subscriptionService.HasActiveSubscriptionAsync(userId);

    return Ok(new { hasActiveSubscription });
  }

  private static List<string> ParseFeatures(string featuresJson)
  {
    try
    {
      if (string.IsNullOrEmpty(featuresJson) || featuresJson == "{}")
      {
        return new List<string>();
      }

      var features = JsonSerializer.Deserialize<List<string>>(featuresJson);
      return features ?? new List<string>();
    }
    catch
    {
      return new List<string>();
    }
  }
}
