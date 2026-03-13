using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Stripe;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Infrastructure.Data;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class StripePaymentServiceTests : IDisposable
{
  private readonly Mock<IConfiguration> _mockConfiguration;
  private readonly Mock<ISubscriptionService> _mockSubscriptionService;
  private readonly Mock<CustomerService> _mockCustomerService;
  private readonly Mock<ILogger<StripePaymentService>> _mockLogger;
  private readonly RadioWashDbContext _dbContext;
  private readonly StripePaymentService _stripePaymentService;

  public StripePaymentServiceTests()
  {
    _mockConfiguration = new Mock<IConfiguration>();
    _mockSubscriptionService = new Mock<ISubscriptionService>();
    _mockCustomerService = new Mock<CustomerService>();
    _mockLogger = new Mock<ILogger<StripePaymentService>>();

    // Setup in-memory database with transaction warnings suppressed
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    _dbContext = new RadioWashDbContext(options);
    _dbContext.Database.EnsureCreated();

    // Setup configuration
    _mockConfiguration.Setup(x => x["FrontendUrl"]).Returns("https://example.com");

    _stripePaymentService = new StripePaymentService(
        _mockConfiguration.Object,
        _mockSubscriptionService.Object,
        _dbContext,
        _mockCustomerService.Object,
        _mockLogger.Object,
        new Stripe.StripeClient("sk_test_123")
    );
  }

  #region CreateCheckoutSession Tests

  [Fact]
  public async Task CreateCheckoutSessionAsync_ShouldLookUpPlanById_NotTrustClientPriceId()
  {
    // Arrange
    var userId = 1;
    var planId = 5;
    var plan = new SubscriptionPlan
    {
      Id = planId,
      Name = "Pro",
      PriceInCents = 1999,
      BillingPeriod = "monthly",
      StripePriceId = "price_server_side_123",
      IsActive = true,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    _mockSubscriptionService.Setup(x => x.GetPlanByIdAsync(planId))
        .ReturnsAsync(plan);

    // Act & Assert — The method should look up the plan by ID, not accept a client price ID.
    // The actual Stripe call will fail since we're using a test key, but the lookup happens first.
    try
    {
      await _stripePaymentService.CreateCheckoutSessionAsync(userId, planId);
    }
    catch (Stripe.StripeException ex)
    {
      // Expected — test Stripe key won't work for real API calls.
      // Verify the exception came from the Stripe API call (not plan lookup).
      Assert.Contains("API Key", ex.Message);
    }

    // Verify server-side plan lookup happened
    _mockSubscriptionService.Verify(x => x.GetPlanByIdAsync(planId), Times.Once);
  }

  [Fact]
  public async Task CreateCheckoutSessionAsync_WithNonExistentPlan_ShouldThrow()
  {
    // Arrange
    var userId = 1;
    var planId = 999;

    _mockSubscriptionService.Setup(x => x.GetPlanByIdAsync(planId))
        .ReturnsAsync((SubscriptionPlan?)null);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.CreateCheckoutSessionAsync(userId, planId));

    Assert.Contains("not found", exception.Message);
  }

  [Fact]
  public async Task CreateCheckoutSessionAsync_WithInactivePlan_ShouldThrow()
  {
    // Arrange
    var userId = 1;
    var planId = 5;
    var plan = new SubscriptionPlan
    {
      Id = planId,
      Name = "Deprecated",
      PriceInCents = 999,
      BillingPeriod = "monthly",
      StripePriceId = "price_old",
      IsActive = false,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    _mockSubscriptionService.Setup(x => x.GetPlanByIdAsync(planId))
        .ReturnsAsync(plan);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _stripePaymentService.CreateCheckoutSessionAsync(userId, planId));

    Assert.Contains("not active", exception.Message);
  }

  [Fact]
  public async Task CreateCheckoutSessionAsync_WithStripeError_ShouldThrowStripeException()
  {
    // Arrange
    var userId = 1;
    var planId = 1;
    var plan = new SubscriptionPlan
    {
      Id = planId,
      Name = "Sync Plan",
      PriceInCents = 500,
      BillingPeriod = "monthly",
      StripePriceId = "price_test_123",
      IsActive = true,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    _mockSubscriptionService.Setup(x => x.GetPlanByIdAsync(planId))
        .ReturnsAsync(plan);

    // Act & Assert — the test Stripe key will cause a StripeException
    var exception = await Assert.ThrowsAsync<StripeException>(
        () => _stripePaymentService.CreateCheckoutSessionAsync(userId, planId));

    // Verify it's a Stripe API error (not a plan lookup error)
    Assert.NotNull(exception);
  }

  #endregion

  public void Dispose()
  {
    _dbContext.Dispose();
  }
}
