using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Controllers;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
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

    _controller = new SubscriptionController(
        _context,
        _mockSubscriptionService.Object,
        _mockPaymentService.Object,
        _mockLogger.Object
    );

    // Setup authenticated user context
    SetupAuthenticatedUser();
  }

  public void Dispose()
  {
    _context.Dispose();
  }

  private void SetupAuthenticatedUser()
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

    var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "test-supabase-id")
        };
    var identity = new ClaimsIdentity(claims, "TestAuth");
    var principal = new ClaimsPrincipal(identity);

    _controller.ControllerContext = new ControllerContext()
    {
      HttpContext = new DefaultHttpContext() { User = principal }
    };
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

  [Fact]
  public async Task CreateCheckoutSession_WithValidRequest_ShouldReturnOkWithCheckoutUrl()
  {
    // Arrange
    var request = new CreateCheckoutDto
    {
      PlanPriceId = "price_test123"
    };
    var checkoutUrl = "https://checkout.stripe.com/test";

    _mockPaymentService.Setup(x => x.CreateCheckoutSessionAsync(1, request.PlanPriceId))
        .ReturnsAsync(checkoutUrl);

    // Act
    var result = await _controller.CreateCheckoutSession(request);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var response = okResult.Value;
    Assert.NotNull(response);
    var checkoutUrlProperty = response.GetType().GetProperty("checkoutUrl");
    Assert.Equal(checkoutUrl, checkoutUrlProperty?.GetValue(response));
  }

  [Fact]
  public async Task CreateCheckoutSession_WhenPaymentServiceThrows_ShouldReturnBadRequest()
  {
    // Arrange
    var request = new CreateCheckoutDto
    {
      PlanPriceId = "invalid_price_id"
    };

    _mockPaymentService.Setup(x => x.CreateCheckoutSessionAsync(1, request.PlanPriceId))
        .ThrowsAsync(new Exception("Invalid price ID"));

    // Act
    var result = await _controller.CreateCheckoutSession(request);

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
    var response = badRequestResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal("Failed to create checkout session", errorProperty?.GetValue(response));
  }

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
    var portalUrlProperty = response.GetType().GetProperty("portalUrl");
    Assert.Equal(portalUrl, portalUrlProperty?.GetValue(response));
  }

  [Fact]
  public async Task CancelSubscription_WithValidUserId_ShouldReturnOk()
  {
    // Arrange
    var canceledSubscription = CreateUserSubscription(1, SubscriptionStatus.Canceled);
    _mockSubscriptionService.Setup(x => x.CancelSubscriptionAsync(1))
        .ReturnsAsync(canceledSubscription);

    // Act
    var result = await _controller.CancelSubscription();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var response = okResult.Value;
    Assert.NotNull(response);
    var messageProperty = response.GetType().GetProperty("message");
    Assert.Equal("Subscription canceled successfully", messageProperty?.GetValue(response));
  }

  [Fact]
  public async Task CancelSubscription_WhenServiceThrows_ShouldReturnBadRequest()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.CancelSubscriptionAsync(1))
        .ThrowsAsync(new Exception("Cancellation failed"));

    // Act
    var result = await _controller.CancelSubscription();

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
    var response = badRequestResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal("Failed to cancel subscription", errorProperty?.GetValue(response));
  }

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
  public async Task HandleStripeWebhook_WhenServiceThrows_ShouldReturnBadRequest()
  {
    // Arrange
    var payload = "invalid_payload";
    var signature = "invalid_signature";

    // Setup HTTP context
    var context = new DefaultHttpContext();
    context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
    context.Request.Headers["Stripe-Signature"] = signature;
    _controller.ControllerContext.HttpContext = context;

    _mockPaymentService.Setup(x => x.HandleWebhookAsync(payload, signature))
        .ThrowsAsync(new Exception("Invalid webhook"));

    // Act
    var result = await _controller.HandleStripeWebhook();

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
    Assert.Equal("Webhook processing failed", badRequestResult.Value);
  }

  private static SubscriptionPlan CreateSubscriptionPlan(int id, string name)
  {
    return new SubscriptionPlan
    {
      Id = id,
      Name = name,
      PriceInCents = 999,
      BillingPeriod = "monthly",
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
    return new UserSubscription
    {
      Id = 1,
      UserId = userId,
      PlanId = 1,
      Plan = CreateSubscriptionPlan(1, "Basic"),
      StripeSubscriptionId = "sub_test123",
      StripeCustomerId = "cus_test123",
      Status = SubscriptionStatus.Active,
      CurrentPeriodStart = DateTime.UtcNow.AddDays(-30),
      CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  [Fact]
  public async Task CreatePortalSession_WithNoActiveSubscription_ShouldReturnBadRequest()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.GetActiveSubscriptionAsync(1))
        .ReturnsAsync((UserSubscription?)null);

    // Act
    var result = await _controller.CreatePortalSession();

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
    var response = badRequestResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal("No active subscription found", errorProperty?.GetValue(response));
  }

  [Fact]
  public async Task GetSubscriptionStatus_WithActiveSubscription_ShouldReturnOkWithTrue()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(true);

    // Act
    var result = await _controller.GetSubscriptionStatus();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var response = okResult.Value;
    Assert.NotNull(response);
    var hasActiveSubscriptionProperty = response.GetType().GetProperty("hasActiveSubscription");
    Assert.True((bool)hasActiveSubscriptionProperty?.GetValue(response)!);
  }

  [Fact]
  public async Task GetSubscriptionStatus_WithNoActiveSubscription_ShouldReturnOkWithFalse()
  {
    // Arrange
    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(false);

    // Act
    var result = await _controller.GetSubscriptionStatus();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var response = okResult.Value;
    Assert.NotNull(response);
    var hasActiveSubscriptionProperty = response.GetType().GetProperty("hasActiveSubscription");
    Assert.False((bool)hasActiveSubscriptionProperty?.GetValue(response)!);
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
