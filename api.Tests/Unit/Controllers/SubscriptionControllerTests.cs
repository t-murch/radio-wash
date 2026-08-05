using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Controllers;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Exceptions;
using RadioWash.Api.Services.Interfaces;
using System.Security.Claims;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Controllers;

public class SubscriptionControllerTests : IDisposable
{
  private readonly Mock<ISubscriptionService> _mockSubscriptionService;
  private readonly Mock<IPaymentService> _mockPaymentService;
  private readonly Mock<ILogger<SubscriptionController>> _mockLogger;
  private readonly RadioWashDbContext _context;
  private readonly SubscriptionController _controller;

  public SubscriptionControllerTests()
  {
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;
    _context = new RadioWashDbContext(options);

    _mockSubscriptionService = new Mock<ISubscriptionService>();
    _mockPaymentService = new Mock<IPaymentService>();
    _mockLogger = new Mock<ILogger<SubscriptionController>>();

    SeedAuthenticatedUser();
    _controller = CreateController(checkoutEnabled: true);
  }

  public void Dispose()
  {
    _context.Dispose();
  }

  private void SeedAuthenticatedUser()
  {
    var user = new User
    {
      Id = 1,
      SupabaseId = "test-supabase-id",
      DisplayName = "Test User",
      Email = "test@example.com",
      CreatedAt = DateTime.UtcNow
    };
    _context.Users.Add(user);
    _context.SaveChanges();
  }

  private SubscriptionController CreateController(bool checkoutEnabled)
  {
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["Features:CheckoutEnabled"] = checkoutEnabled.ToString().ToLowerInvariant()
        })
        .Build();

    var controller = new SubscriptionController(
        _context,
        _mockSubscriptionService.Object,
        _mockPaymentService.Object,
        configuration,
        _mockLogger.Object
    );

    var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "test-supabase-id")
        };
    var identity = new ClaimsIdentity(claims, "TestAuth");
    var principal = new ClaimsPrincipal(identity);

    controller.ControllerContext = new ControllerContext()
    {
      HttpContext = new DefaultHttpContext() { User = principal }
    };

    return controller;
  }

  private static object? GetProperty(object response, string name)
  {
    return response.GetType().GetProperty(name)?.GetValue(response);
  }

  [Fact]
  public async Task GetAvailablePlans_ShouldReturnOkWithPlans()
  {
    // Arrange
    var plans = new List<SubscriptionPlan>
        {
            CreateSubscriptionPlan(1, "Basic"),
            CreateSubscriptionPlan(2, "Premium")
        };
    _mockSubscriptionService.Setup(x => x.GetAvailablePlansAsync())
        .ReturnsAsync(plans);

    // Act
    var result = await _controller.GetAvailablePlans();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var returnedPlans = Assert.IsAssignableFrom<IEnumerable<SubscriptionPlanDto>>(okResult.Value);
    Assert.Equal(2, returnedPlans.Count());
  }

  [Fact]
  public async Task GetCurrentSubscription_WithSubscription_ShouldReturnOkWithSubscription()
  {
    // Arrange
    var subscription = CreateUserSubscriptionWithPlan(1);
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);

    // Act
    var result = await _controller.GetCurrentSubscription();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var returnedSubscription = Assert.IsType<UserSubscriptionDto>(okResult.Value);
    Assert.Equal(SubscriptionStatus.Active, returnedSubscription.Status);
    Assert.False(returnedSubscription.CancelAtPeriodEnd);
  }

  [Fact]
  public async Task GetCurrentSubscription_WithCancellationScheduled_ShouldExposeCancelAtPeriodEnd()
  {
    // Arrange
    var subscription = CreateUserSubscriptionWithPlan(1);
    subscription.CancelAtPeriodEnd = true;
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);

    // Act
    var result = await _controller.GetCurrentSubscription();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var returnedSubscription = Assert.IsType<UserSubscriptionDto>(okResult.Value);
    Assert.True(returnedSubscription.CancelAtPeriodEnd);
  }

  [Fact]
  public async Task GetCurrentSubscription_WithNoSubscription_ShouldReturnOkWithNull()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync((UserSubscription?)null);

    // Act
    var result = await _controller.GetCurrentSubscription();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Null(okResult.Value);
  }

  #region CreateCheckoutSession

  [Fact]
  public async Task CreateCheckoutSession_WithCheckoutDisabled_ShouldReturnServiceUnavailable()
  {
    // Arrange - the kill switch short-circuits before any service call
    var controller = CreateController(checkoutEnabled: false);

    // Act
    var result = await controller.CreateCheckoutSession(new CreateCheckoutDto());

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
    _mockPaymentService.Verify(
        x => x.CreateCheckoutSessionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()),
        Times.Never);
  }

  [Fact]
  public async Task CreateCheckoutSession_WithActiveSubscription_ShouldReturnConflict()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(true);

    // Act
    var result = await _controller.CreateCheckoutSession(new CreateCheckoutDto());

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
    _mockPaymentService.Verify(
        x => x.CreateCheckoutSessionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()),
        Times.Never);
  }

  [Fact]
  public async Task CreateCheckoutSession_WithNullPlanId_ShouldUseFirstAvailablePlan()
  {
    // Arrange - ClientRequestId must be a UUID (the frontend sends crypto.randomUUID())
    var clientRequestId = "1c1b7f6e-9f6c-4f4e-8a58-0c8f4a1d2b3c";
    var plan = CreateSubscriptionPlan(1, "Basic", stripePriceId: "price_default");
    var checkoutUrl = "https://checkout.stripe.com/test";

    _mockSubscriptionService.Setup(x => x.GetAvailablePlansAsync())
        .ReturnsAsync(new[] { plan });
    _mockPaymentService.Setup(x => x.CreateCheckoutSessionAsync(1, "price_default", clientRequestId))
        .ReturnsAsync(checkoutUrl);

    // Act
    var result = await _controller.CreateCheckoutSession(
        new CreateCheckoutDto { PlanId = null, ClientRequestId = clientRequestId });

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.NotNull(okResult.Value);
    Assert.Equal(checkoutUrl, GetProperty(okResult.Value!, "checkoutUrl"));
    _mockPaymentService.Verify(x => x.CreateCheckoutSessionAsync(1, "price_default", clientRequestId), Times.Once);
    _mockSubscriptionService.Verify(x => x.GetPlanByIdAsync(It.IsAny<int>()), Times.Never);
  }

  [Fact]
  public async Task CreateCheckoutSession_WithExplicitPlanId_ShouldResolvePlanById()
  {
    // Arrange
    var plan = CreateSubscriptionPlan(2, "Premium", stripePriceId: "price_premium");
    var checkoutUrl = "https://checkout.stripe.com/test";

    _mockSubscriptionService.Setup(x => x.GetPlanByIdAsync(2))
        .ReturnsAsync(plan);
    _mockPaymentService.Setup(x => x.CreateCheckoutSessionAsync(1, "price_premium", null))
        .ReturnsAsync(checkoutUrl);

    // Act
    var result = await _controller.CreateCheckoutSession(new CreateCheckoutDto { PlanId = 2 });

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.Equal(checkoutUrl, GetProperty(okResult.Value!, "checkoutUrl"));
    _mockSubscriptionService.Verify(x => x.GetAvailablePlansAsync(), Times.Never);
  }

  [Theory]
  [InlineData("not-a-uuid")]
  [InlineData("checkout-1-injected'; DROP TABLE")]
  public async Task CreateCheckoutSession_WithNonUuidClientRequestId_ShouldReturnBadRequest(string clientRequestId)
  {
    // Arrange - ClientRequestId feeds Stripe's idempotency key (255-char cap); arbitrary
    // client strings must be rejected before any Stripe call.
    var result = await _controller.CreateCheckoutSession(new CreateCheckoutDto
    {
      ClientRequestId = clientRequestId
    });

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    _mockPaymentService.Verify(x => x.CreateCheckoutSessionAsync(
        It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
  }

  [Fact]
  public async Task CreateCheckoutSession_WithUnknownPlanId_ShouldReturnBadRequest()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.GetPlanByIdAsync(99))
        .ReturnsAsync((SubscriptionPlan?)null);

    // Act
    var result = await _controller.CreateCheckoutSession(new CreateCheckoutDto { PlanId = 99 });

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
  }

  [Fact]
  public async Task CreateCheckoutSession_WithInactivePlan_ShouldReturnBadRequest()
  {
    // Arrange
    var plan = CreateSubscriptionPlan(2, "Retired", stripePriceId: "price_retired");
    plan.IsActive = false;
    _mockSubscriptionService.Setup(x => x.GetPlanByIdAsync(2))
        .ReturnsAsync(plan);

    // Act
    var result = await _controller.CreateCheckoutSession(new CreateCheckoutDto { PlanId = 2 });

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    _mockPaymentService.Verify(
        x => x.CreateCheckoutSessionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()),
        Times.Never);
  }

  [Fact]
  public async Task CreateCheckoutSession_WithPlanMissingStripePrice_ShouldReturnBadRequest()
  {
    // Arrange
    var plan = CreateSubscriptionPlan(2, "Unpriced", stripePriceId: null);
    _mockSubscriptionService.Setup(x => x.GetPlanByIdAsync(2))
        .ReturnsAsync(plan);

    // Act
    var result = await _controller.CreateCheckoutSession(new CreateCheckoutDto { PlanId = 2 });

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
  }

  [Fact]
  public async Task CreateCheckoutSession_WhenPaymentServiceThrows_ShouldReturnInternalServerError()
  {
    // Arrange
    var plan = CreateSubscriptionPlan(1, "Basic", stripePriceId: "price_default");
    _mockSubscriptionService.Setup(x => x.GetAvailablePlansAsync())
        .ReturnsAsync(new[] { plan });
    _mockPaymentService.Setup(x => x.CreateCheckoutSessionAsync(1, "price_default", null))
        .ThrowsAsync(new Exception("Stripe unavailable"));

    // Act
    var result = await _controller.CreateCheckoutSession(new CreateCheckoutDto());

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
  }

  #endregion

  #region CompleteCheckout

  [Fact]
  public async Task CompleteCheckout_WithEmptySessionId_ShouldReturnBadRequest()
  {
    // Act
    var result = await _controller.CompleteCheckout(new CompleteCheckoutDto { SessionId = "" });

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    _mockPaymentService.Verify(x => x.GetCheckoutSessionAsync(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task CompleteCheckout_WhenSessionNotFoundOnStripe_ShouldReturnNotFound()
  {
    // Arrange - only a genuine resource_missing maps to 404
    _mockPaymentService.Setup(x => x.GetCheckoutSessionAsync("cs_missing"))
        .ThrowsAsync(new Stripe.StripeException("No such checkout session")
        {
          StripeError = new Stripe.StripeError { Code = "resource_missing" }
        });

    // Act
    var result = await _controller.CompleteCheckout(new CompleteCheckoutDto { SessionId = "cs_missing" });

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
  }

  [Fact]
  public async Task CompleteCheckout_WhenStripeFailsTransiently_ShouldReturn500NotNotFound()
  {
    // Arrange - a Stripe outage right after the user PAID must read as retryable, not as
    // "your session doesn't exist"
    _mockPaymentService.Setup(x => x.GetCheckoutSessionAsync("cs_1"))
        .ThrowsAsync(new Stripe.StripeException("Stripe unavailable"));

    // Act
    var result = await _controller.CompleteCheckout(new CompleteCheckoutDto { SessionId = "cs_1" });

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
  }

  [Fact]
  public async Task CompleteCheckout_WithMissingUserMetadata_ShouldReturnForbidden()
  {
    // Arrange - a session without a userId claim can't be attributed to the caller
    _mockPaymentService.Setup(x => x.GetCheckoutSessionAsync("cs_1"))
        .ReturnsAsync(new Stripe.Checkout.Session { Id = "cs_1" });

    // Act
    var result = await _controller.CompleteCheckout(new CompleteCheckoutDto { SessionId = "cs_1" });

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    _mockSubscriptionService.Verify(
        x => x.SyncFromStripeAsync(It.IsAny<Stripe.Subscription>(), It.IsAny<Func<Task<int?>>?>()),
        Times.Never);
  }

  [Fact]
  public async Task CompleteCheckout_WithAnotherUsersSession_ShouldReturnForbidden()
  {
    // Arrange
    _mockPaymentService.Setup(x => x.GetCheckoutSessionAsync("cs_1"))
        .ReturnsAsync(new Stripe.Checkout.Session
        {
          Id = "cs_1",
          Metadata = new Dictionary<string, string> { { "userId", "999" } }
        });

    // Act
    var result = await _controller.CompleteCheckout(new CompleteCheckoutDto { SessionId = "cs_1" });

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    _mockSubscriptionService.Verify(
        x => x.SyncFromStripeAsync(It.IsAny<Stripe.Subscription>(), It.IsAny<Func<Task<int?>>?>()),
        Times.Never);
  }

  [Fact]
  public async Task CompleteCheckout_WithSubscription_ShouldSyncAndReturnStatus()
  {
    // Arrange
    var stripeSubscription = new Stripe.Subscription { Id = "sub_1" };
    _mockPaymentService.Setup(x => x.GetCheckoutSessionAsync("cs_1"))
        .ReturnsAsync(new Stripe.Checkout.Session
        {
          Id = "cs_1",
          Metadata = new Dictionary<string, string> { { "userId", "1" } },
          Subscription = stripeSubscription
        });
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(true);
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(CreateUserSubscriptionWithPlan(1));

    // Act
    var result = await _controller.CompleteCheckout(new CompleteCheckoutDto { SessionId = "cs_1" });

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.NotNull(okResult.Value);
    Assert.True((bool)GetProperty(okResult.Value!, "hasActiveSubscription")!);
    Assert.Equal("Basic", GetProperty(okResult.Value!, "planName"));
    _mockSubscriptionService.Verify(
        x => x.SyncFromStripeAsync(stripeSubscription, It.IsAny<Func<Task<int?>>?>()),
        Times.Once);
  }

  [Fact]
  public async Task CompleteCheckout_WithoutSubscriptionOnSession_ShouldNotSyncButStillReturnOk()
  {
    // Arrange - payment may still be processing; the webhook will finish the job
    _mockPaymentService.Setup(x => x.GetCheckoutSessionAsync("cs_1"))
        .ReturnsAsync(new Stripe.Checkout.Session
        {
          Id = "cs_1",
          Metadata = new Dictionary<string, string> { { "userId", "1" } },
          Subscription = null
        });

    // Act
    var result = await _controller.CompleteCheckout(new CompleteCheckoutDto { SessionId = "cs_1" });

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.NotNull(okResult.Value);
    Assert.False((bool)GetProperty(okResult.Value!, "hasActiveSubscription")!);
    _mockSubscriptionService.Verify(
        x => x.SyncFromStripeAsync(It.IsAny<Stripe.Subscription>(), It.IsAny<Func<Task<int?>>?>()),
        Times.Never);
  }

  #endregion

  [Fact]
  public async Task CreatePortalSession_WithActiveSubscription_ShouldReturnOkWithPortalUrl()
  {
    // Arrange
    var subscription = CreateUserSubscriptionWithPlan(1);
    subscription.StripeCustomerId = "cus_test123";
    var portalUrl = "https://billing.stripe.com/session/test";

    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);
    _mockPaymentService.Setup(x => x.CreatePortalSessionAsync(subscription.StripeCustomerId))
        .ReturnsAsync(portalUrl);

    // Act
    var result = await _controller.CreatePortalSession();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var response = okResult.Value;
    Assert.NotNull(response);
    Assert.Equal(portalUrl, GetProperty(response!, "portalUrl"));
  }

  #region CancelSubscription

  [Fact]
  public async Task CancelSubscription_WithNoSubscription_ShouldReturnNotFound()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync((UserSubscription?)null);

    // Act
    var result = await _controller.CancelSubscription();

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    _mockPaymentService.Verify(x => x.CancelAtPeriodEndAsync(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task CancelSubscription_WithNonEntitledStatus_ShouldReturnNotFound()
  {
    // Arrange - a canceled/past_due subscription has nothing left to cancel
    var subscription = CreateUserSubscription(1, SubscriptionStatus.Canceled);
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);

    // Act
    var result = await _controller.CancelSubscription();

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    _mockPaymentService.Verify(x => x.CancelAtPeriodEndAsync(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task CancelSubscription_WithMissingStripeSubscriptionId_ShouldReturnNotFound()
  {
    // Arrange
    var subscription = CreateUserSubscription(1);
    subscription.StripeSubscriptionId = null;
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);

    // Act
    var result = await _controller.CancelSubscription();

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
  }

  [Fact]
  public async Task CancelSubscription_WhenAlreadyScheduled_ShouldReturnOkWithoutCallingStripe()
  {
    // Arrange - idempotent success, no second Stripe round-trip
    var subscription = CreateUserSubscription(1);
    subscription.CancelAtPeriodEnd = true;
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);

    // Act
    var result = await _controller.CancelSubscription();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.NotNull(okResult.Value);
    Assert.True((bool)GetProperty(okResult.Value!, "cancelAtPeriodEnd")!);
    Assert.Equal(subscription.CurrentPeriodEnd, GetProperty(okResult.Value!, "activeUntil"));
    _mockPaymentService.Verify(x => x.CancelAtPeriodEndAsync(It.IsAny<string>()), Times.Never);
    _mockSubscriptionService.Verify(x => x.MarkCancellationRequestedAsync(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task CancelSubscription_HappyPath_ShouldCallStripeBeforePersistingLocalFlag()
  {
    // Arrange
    var subscription = CreateUserSubscription(1);
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);

    var callOrder = new List<string>();
    _mockPaymentService.Setup(x => x.CancelAtPeriodEndAsync("sub_test123"))
        .Callback(() => callOrder.Add("stripe"))
        .Returns(Task.CompletedTask);
    _mockSubscriptionService.Setup(x => x.MarkCancellationRequestedAsync("sub_test123"))
        .Callback(() => callOrder.Add("local"))
        .ReturnsAsync(subscription);

    // Act
    var result = await _controller.CancelSubscription();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    Assert.NotNull(okResult.Value);
    Assert.True((bool)GetProperty(okResult.Value!, "cancelAtPeriodEnd")!);
    Assert.Equal(subscription.CurrentPeriodEnd, GetProperty(okResult.Value!, "activeUntil"));

    _mockPaymentService.Verify(x => x.CancelAtPeriodEndAsync("sub_test123"), Times.Once);
    _mockSubscriptionService.Verify(x => x.MarkCancellationRequestedAsync("sub_test123"), Times.Once);
    // Stripe must accept the cancellation before the local flag is persisted
    Assert.Equal(new[] { "stripe", "local" }, callOrder);
  }

  [Fact]
  public async Task CancelSubscription_WhenStripeCallFails_ShouldReturn500AndNotPersistLocalFlag()
  {
    // Arrange
    var subscription = CreateUserSubscription(1);
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);
    _mockPaymentService.Setup(x => x.CancelAtPeriodEndAsync("sub_test123"))
        .ThrowsAsync(new Stripe.StripeException("Stripe API unavailable"));

    // Act
    var result = await _controller.CancelSubscription();

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
    _mockSubscriptionService.Verify(x => x.MarkCancellationRequestedAsync(It.IsAny<string>()), Times.Never);
  }

  #endregion

  [Fact]
  public async Task HandleStripeWebhook_WithValidPayload_ShouldReturnOk()
  {
    // Arrange
    var payload = "{\"type\": \"invoice.payment_succeeded\"}";
    var signature = "t=1234567890,v1=abcd1234";

    // Setup HTTP context with request body and headers
    var context = new DefaultHttpContext();
    context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
    context.Request.Headers["Stripe-Signature"] = signature;
    _controller.ControllerContext.HttpContext = context;

    _mockPaymentService.Setup(x => x.HandleWebhookAsync(payload, signature))
        .Returns(Task.CompletedTask);

    // Act
    var result = await _controller.HandleStripeWebhook();

    // Assert
    Assert.IsType<OkResult>(result);
    _mockPaymentService.Verify(x => x.HandleWebhookAsync(payload, signature), Times.Once);
  }

  [Fact]
  public async Task HandleStripeWebhook_WhenServiceThrows_ShouldReturnInternalServerError()
  {
    // Arrange - a generic processing failure must return 500 so Stripe redelivers
    var payload = "{\"type\": \"invoice.payment_succeeded\"}";
    var signature = "t=1234567890,v1=abcd1234";

    // Setup HTTP context
    var context = new DefaultHttpContext();
    context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
    context.Request.Headers["Stripe-Signature"] = signature;
    _controller.ControllerContext.HttpContext = context;

    _mockPaymentService.Setup(x => x.HandleWebhookAsync(payload, signature))
        .ThrowsAsync(new Exception("Transient processing failure"));

    // Act
    var result = await _controller.HandleStripeWebhook();

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    Assert.Equal("Webhook processing failed", objectResult.Value);
  }

  [Fact]
  public async Task HandleStripeWebhook_WithInvalidSignature_ShouldReturnBadRequest()
  {
    // Arrange - a signature verification failure is a permanent rejection (400)
    var payload = "tampered_payload";
    var signature = "t=1234567890,v1=invalid";

    // Setup HTTP context
    var context = new DefaultHttpContext();
    context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
    context.Request.Headers["Stripe-Signature"] = signature;
    _controller.ControllerContext.HttpContext = context;

    _mockPaymentService.Setup(x => x.HandleWebhookAsync(payload, signature))
        .ThrowsAsync(new WebhookSignatureVerificationException(
            "Stripe webhook signature verification failed",
            new Stripe.StripeException("Signature mismatch")));

    // Act
    var result = await _controller.HandleStripeWebhook();

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
    Assert.Equal("Invalid Stripe signature", badRequestResult.Value);
  }

  private static SubscriptionPlan CreateSubscriptionPlan(int id, string name, string? stripePriceId = "price_test")
  {
    return new SubscriptionPlan
    {
      Id = id,
      Name = name,
      PriceInCents = 999,
      BillingPeriod = "monthly",
      StripePriceId = stripePriceId,
      IsActive = true,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  private static UserSubscription CreateUserSubscription(int userId, string status = SubscriptionStatus.Active)
  {
    return new UserSubscription
    {
      Id = 1,
      UserId = userId,
      PlanId = 1,
      StripeSubscriptionId = "sub_test123",
      StripeCustomerId = "cus_test123",
      Status = status,
      CurrentPeriodStart = DateTime.UtcNow.AddDays(-30),
      CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  private static UserSubscription CreateUserSubscriptionWithPlan(int userId)
  {
    var subscription = CreateUserSubscription(userId);
    subscription.Plan = CreateSubscriptionPlan(1, "Basic");
    return subscription;
  }

  [Fact]
  public async Task CreatePortalSession_WithNoActiveSubscription_ShouldReturnNotFound()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync((UserSubscription?)null);

    // Act
    var result = await _controller.CreatePortalSession();

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
  }

  [Fact]
  public async Task CreatePortalSession_WhenStripeFails_ShouldReturn500()
  {
    // Arrange - portal failure is server-side (Stripe outage, unconfigured Dashboard
    // portal), not a client error
    var subscription = CreateUserSubscription(1);
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);
    _mockPaymentService.Setup(x => x.CreatePortalSessionAsync(It.IsAny<string>()))
        .ThrowsAsync(new Stripe.StripeException("portal not configured"));

    // Act
    var result = await _controller.CreatePortalSession();

    // Assert
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
  }

  [Fact]
  public async Task GetSubscriptionStatus_WithActiveSubscription_ShouldReturnFullPayload()
  {
    // Arrange
    var subscription = CreateUserSubscriptionWithPlan(1);
    subscription.CancelAtPeriodEnd = true;
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(true);
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync(subscription);

    // Act
    var result = await _controller.GetSubscriptionStatus();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var response = okResult.Value;
    Assert.NotNull(response);
    Assert.True((bool)GetProperty(response!, "hasActiveSubscription")!);
    Assert.Equal(subscription.Id, GetProperty(response!, "subscriptionId"));
    Assert.Equal("Basic", GetProperty(response!, "planName"));
    Assert.Equal(SubscriptionStatus.Active, GetProperty(response!, "status"));
    Assert.Equal(subscription.CurrentPeriodEnd, GetProperty(response!, "currentPeriodEnd"));
    Assert.True((bool)GetProperty(response!, "cancelAtPeriodEnd")!);
  }

  [Fact]
  public async Task GetSubscriptionStatus_WithNoActiveSubscription_ShouldReturnEmptyPayload()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(false);
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync((UserSubscription?)null);

    // Act
    var result = await _controller.GetSubscriptionStatus();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var response = okResult.Value;
    Assert.NotNull(response);
    Assert.False((bool)GetProperty(response!, "hasActiveSubscription")!);
    Assert.Null(GetProperty(response!, "subscriptionId"));
    Assert.Null(GetProperty(response!, "planName"));
    Assert.Null(GetProperty(response!, "status"));
    Assert.False((bool)GetProperty(response!, "cancelAtPeriodEnd")!);
  }

  [Fact]
  public async Task HandleStripeWebhook_WithMissingSignature_ShouldReturnBadRequest()
  {
    // Arrange
    var payload = "{\"type\": \"invoice.payment_succeeded\"}";

    // Setup HTTP context without signature header
    var context = new DefaultHttpContext();
    context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
    _controller.ControllerContext.HttpContext = context;

    // Act
    var result = await _controller.HandleStripeWebhook();

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
    Assert.Equal("Missing Stripe signature", badRequestResult.Value);
  }
}
