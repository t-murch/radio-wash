using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Exceptions;
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
  private readonly IConfiguration _configuration;
  private readonly ILogger<SubscriptionController> _logger;

  public SubscriptionController(
      RadioWashDbContext dbContext,
      ISubscriptionService subscriptionService,
      IPaymentService paymentService,
      IConfiguration configuration,
      ILogger<SubscriptionController> logger) : base(dbContext, logger)
  {
    _subscriptionService = subscriptionService;
    _paymentService = paymentService;
    _configuration = configuration;
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
      CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
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
  [EnableRateLimiting("checkout")]
  public async Task<ActionResult> CreateCheckoutSession([FromBody] CreateCheckoutDto dto)
  {
    var userId = GetCurrentUserId();

    // Kill switch: lets checkout be disabled via an app-settings change without a deploy.
    if (!_configuration.GetValue("Features:CheckoutEnabled", true))
    {
      return Problem(
        title: "Checkout disabled",
        detail: "Subscriptions are temporarily unavailable. Please try again later.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        type: "https://radiowash.app/problems/checkout-disabled");
    }

    if (await _subscriptionService.HasActiveSubscriptionAsync(userId))
    {
      return Problem(
        title: "Already subscribed",
        detail: "You already have an active subscription.",
        statusCode: StatusCodes.Status409Conflict,
        type: "https://radiowash.app/problems/already-subscribed");
    }

    // The Stripe price is resolved server-side from the local plan — client-supplied price
    // ids are never forwarded to Stripe.
    var plan = dto.PlanId.HasValue
      ? await _subscriptionService.GetPlanByIdAsync(dto.PlanId.Value)
      : (await _subscriptionService.GetAvailablePlansAsync()).FirstOrDefault();

    if (plan is not { IsActive: true } || string.IsNullOrEmpty(plan.StripePriceId))
    {
      return Problem(
        title: "Plan unavailable",
        detail: "The requested subscription plan is not available.",
        statusCode: StatusCodes.Status400BadRequest,
        type: "https://radiowash.app/problems/plan-unavailable");
    }

    try
    {
      var checkoutUrl = await _paymentService.CreateCheckoutSessionAsync(userId, plan.StripePriceId, dto.ClientRequestId);
      return Ok(new { checkoutUrl });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create checkout session for user {UserId}", userId);
      return Problem(
        title: "Checkout failed",
        detail: "Could not start checkout. Please try again.",
        statusCode: StatusCodes.Status500InternalServerError);
    }
  }

  // Called by the success page with the session_id Stripe appended to the redirect. Pulls
  // the session (and subscription) from Stripe and syncs it locally, so activation doesn't
  // depend on the webhook having already arrived. Idempotent.
  [HttpPost("checkout/complete")]
  [EnableRateLimiting("checkout")]
  public async Task<ActionResult> CompleteCheckout([FromBody] CompleteCheckoutDto dto)
  {
    var userId = GetCurrentUserId();

    if (string.IsNullOrEmpty(dto.SessionId))
    {
      return Problem(
        title: "Missing session id",
        detail: "A checkout session id is required.",
        statusCode: StatusCodes.Status400BadRequest);
    }

    Stripe.Checkout.Session session;
    try
    {
      session = await _paymentService.GetCheckoutSessionAsync(dto.SessionId);
    }
    catch (Stripe.StripeException ex)
    {
      _logger.LogWarning(ex, "Checkout session {SessionId} could not be retrieved for user {UserId}", dto.SessionId, userId);
      return Problem(
        title: "Unknown checkout session",
        detail: "The checkout session could not be found.",
        statusCode: StatusCodes.Status404NotFound);
    }

    if (session.Metadata == null
        || !session.Metadata.TryGetValue("userId", out var sessionUserId)
        || sessionUserId != userId.ToString())
    {
      _logger.LogWarning("User {UserId} attempted to complete checkout session {SessionId} belonging to someone else",
        userId, dto.SessionId);
      return Problem(
        title: "Forbidden",
        detail: "This checkout session does not belong to the current user.",
        statusCode: StatusCodes.Status403Forbidden);
    }

    if (session.Subscription != null)
    {
      await _subscriptionService.SyncFromStripeAsync(session.Subscription);
    }
    else
    {
      _logger.LogInformation("Checkout session {SessionId} has no subscription yet (payment may still be processing)",
        dto.SessionId);
    }

    return Ok(await BuildStatusPayloadAsync(userId));
  }

  [HttpPost("portal")]
  [EnableRateLimiting("checkout")]
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
  [EnableRateLimiting("checkout")]
  public async Task<ActionResult> CancelSubscription()
  {
    var userId = GetCurrentUserId();

    var subscription = await _subscriptionService.GetActiveSubscriptionAsync(userId);
    if (subscription == null
        || !Models.Domain.SubscriptionStatusMapper.IsEntitled(subscription.Status)
        || string.IsNullOrEmpty(subscription.StripeSubscriptionId))
    {
      return Problem(
        title: "No active subscription",
        detail: "There is no active subscription to cancel.",
        statusCode: StatusCodes.Status404NotFound);
    }

    if (subscription.CancelAtPeriodEnd)
    {
      // Already scheduled — idempotent success.
      return Ok(new
      {
        message = "Subscription is already scheduled to cancel at the end of the billing period",
        activeUntil = subscription.CurrentPeriodEnd,
        cancelAtPeriodEnd = true
      });
    }

    try
    {
      // Stripe first: if this fails the user stays subscribed on both sides. The local
      // flag follows only after Stripe accepted the cancellation.
      await _paymentService.CancelAtPeriodEndAsync(subscription.StripeSubscriptionId);
      await _subscriptionService.MarkCancellationRequestedAsync(userId);

      return Ok(new
      {
        message = "Subscription will cancel at the end of the current billing period",
        activeUntil = subscription.CurrentPeriodEnd,
        cancelAtPeriodEnd = true
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to cancel subscription for user {UserId}", userId);
      return Problem(
        title: "Cancellation failed",
        detail: "Could not cancel the subscription. Please try again.",
        statusCode: StatusCodes.Status500InternalServerError);
    }
  }

  [HttpPost("webhook")]
  [AllowAnonymous]
  public async Task<ActionResult> HandleStripeWebhook()
  {
    var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
    var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();

    if (string.IsNullOrEmpty(signature))
    {
      return BadRequest("Missing Stripe signature");
    }

    try
    {
      await _paymentService.HandleWebhookAsync(json, signature);
      return Ok();
    }
    catch (WebhookSignatureVerificationException ex)
    {
      // 400 tells Stripe the delivery is permanently rejected — reserved for payloads
      // that fail authentication.
      _logger.LogWarning(ex, "Rejected Stripe webhook with invalid signature");
      return BadRequest("Invalid Stripe signature");
    }
    catch (Exception ex)
    {
      // 500 makes Stripe redeliver with backoff for up to 3 days; combined with the
      // released idempotency claim, transient failures self-heal.
      _logger.LogError(ex, "Error processing Stripe webhook");
      return StatusCode(StatusCodes.Status500InternalServerError, "Webhook processing failed");
    }
  }

  [HttpGet("status")]
  public async Task<ActionResult> GetSubscriptionStatus()
  {
    var userId = GetCurrentUserId();
    return Ok(await BuildStatusPayloadAsync(userId));
  }

  private async Task<object> BuildStatusPayloadAsync(int userId)
  {
    var hasActiveSubscription = await _subscriptionService.HasActiveSubscriptionAsync(userId);
    var subscription = await _subscriptionService.GetActiveSubscriptionAsync(userId);

    return new
    {
      hasActiveSubscription,
      subscriptionId = subscription?.Id,
      planName = subscription?.Plan?.Name,
      status = subscription?.Status,
      currentPeriodEnd = subscription?.CurrentPeriodEnd,
      cancelAtPeriodEnd = subscription?.CancelAtPeriodEnd ?? false
    };
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
