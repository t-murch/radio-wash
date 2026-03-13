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
  private readonly IWebhookOrchestrator _webhookOrchestrator;
  private readonly ILogger<SubscriptionController> _logger;

  public SubscriptionController(
      RadioWashDbContext dbContext,
      ISubscriptionService subscriptionService,
      IPaymentService paymentService,
      IWebhookOrchestrator webhookOrchestrator,
      ILogger<SubscriptionController> logger) : base(dbContext, logger)
  {
    _subscriptionService = subscriptionService;
    _paymentService = paymentService;
    _webhookOrchestrator = webhookOrchestrator;
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
    catch (InvalidOperationException ex)
    {
      _logger.LogWarning(ex, "Cannot create checkout session for user {UserId}", userId);
      return Problem(
        detail: ex.Message,
        statusCode: StatusCodes.Status400BadRequest
      );
    }
    catch (Stripe.StripeException ex)
    {
      _logger.LogWarning(ex, "Stripe error creating checkout session for user {UserId}", userId);
      return Problem(
        detail: "Upstream payment service error",
        statusCode: StatusCodes.Status502BadGateway
      );
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create checkout session for user {UserId}", userId);
      return Problem(
        detail: "Failed to create checkout session",
        statusCode: StatusCodes.Status500InternalServerError
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
    catch (InvalidOperationException ex)
    {
      _logger.LogWarning(ex, "Cannot create portal session for user {UserId}", userId);
      return Problem(
        detail: ex.Message,
        statusCode: StatusCodes.Status400BadRequest
      );
    }
    catch (Stripe.StripeException ex)
    {
      _logger.LogWarning(ex, "Stripe error creating portal session for user {UserId}", userId);
      return Problem(
        detail: "Upstream payment service error",
        statusCode: StatusCodes.Status502BadGateway
      );
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create portal session for user {UserId}", userId);
      return Problem(
        detail: "Failed to create portal session",
        statusCode: StatusCodes.Status500InternalServerError
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
    catch (InvalidOperationException ex)
    {
      _logger.LogWarning(ex, "Cannot cancel subscription for user {UserId}", userId);
      return Problem(
        detail: ex.Message,
        statusCode: StatusCodes.Status400BadRequest
      );
    }
    catch (Stripe.StripeException ex)
    {
      _logger.LogWarning(ex, "Stripe error canceling subscription for user {UserId}", userId);
      return Problem(
        detail: "Upstream payment service error",
        statusCode: StatusCodes.Status502BadGateway
      );
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to cancel subscription for user {UserId}", userId);
      return Problem(
        detail: "Failed to cancel subscription",
        statusCode: StatusCodes.Status500InternalServerError
      );
    }
  }

  [HttpPost("resume")]
  public async Task<ActionResult> ResumeSubscription()
  {
    var userId = GetCurrentUserId();

    try
    {
      await _subscriptionService.ResumeSubscriptionAsync(userId);
      return Ok(new { message = "Subscription resumed successfully" });
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogWarning(ex, "Cannot resume subscription for user {UserId}", userId);
      return Problem(
        detail: ex.Message,
        statusCode: StatusCodes.Status400BadRequest
      );
    }
    catch (Stripe.StripeException ex)
    {
      _logger.LogWarning(ex, "Stripe error resuming subscription for user {UserId}", userId);
      return Problem(
        detail: "Upstream payment service error",
        statusCode: StatusCodes.Status502BadGateway
      );
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to resume subscription for user {UserId}", userId);
      return Problem(
        detail: "Failed to resume subscription",
        statusCode: StatusCodes.Status500InternalServerError
      );
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

      await _webhookOrchestrator.HandleWebhookAsync(json, signature);
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

  [HttpGet("verify-session")]
  public async Task<ActionResult> VerifyCheckoutSession([FromQuery] string sessionId)
  {
    if (string.IsNullOrEmpty(sessionId))
    {
      return BadRequest(new { error = "Session ID is required" });
    }

    try
    {
      var session = await _paymentService.VerifyCheckoutSessionAsync(sessionId);

      if (session == null)
      {
        return Ok(new { verified = false });
      }

      var userId = GetCurrentUserId();

      // Verify the session belongs to the authenticated user
      if (session.Metadata == null
          || !session.Metadata.TryGetValue("userId", out var sessionUserIdStr)
          || !int.TryParse(sessionUserIdStr, out var sessionUserId))
      {
        _logger.LogWarning("Session {SessionId} has no valid userId metadata", sessionId);
        return Ok(new { verified = false });
      }
      if (sessionUserId != userId)
      {
        _logger.LogWarning("Session {SessionId} belongs to user {SessionUserId}, not {UserId}",
            sessionId, sessionUserId, userId);
        return Ok(new { verified = false });
      }

      var subscription = await _subscriptionService.GetActiveSubscriptionAsync(userId);

      return Ok(new
      {
        verified = true,
        subscription = subscription?.ToDto()
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to verify checkout session {SessionId}", sessionId);
      return Ok(new { verified = false });
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
