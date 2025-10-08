using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;
using System.Text.Json;

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
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create checkout session for user {UserId}", userId);
      return BadRequest(new { error = "Failed to create checkout session" });
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
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create portal session for user {UserId}", userId);
      return BadRequest(new { error = "Failed to create portal session" });
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
