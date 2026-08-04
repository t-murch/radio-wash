using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using Stripe;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class StripeReconciliationServiceTests
{
  private readonly Mock<ISubscriptionService> _mockSubscriptionService;
  private readonly Mock<Stripe.SubscriptionService> _mockStripeSubscriptionService;
  private readonly Mock<CustomerService> _mockCustomerService;
  private readonly StripeReconciliationService _service;

  public StripeReconciliationServiceTests()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["Stripe:SecretKey"] = "sk_test_123"
      })
      .Build();

    _mockSubscriptionService = new Mock<ISubscriptionService>();
    _mockStripeSubscriptionService = new Mock<Stripe.SubscriptionService>();
    _mockCustomerService = new Mock<CustomerService>();

    // Default: nothing local to reconcile, nothing active on Stripe.
    _mockSubscriptionService.Setup(x => x.GetReconcilableSubscriptionsAsync())
      .ReturnsAsync(new List<UserSubscription>());
    _mockStripeSubscriptionService
      .Setup(x => x.ListAutoPagingAsync(It.IsAny<SubscriptionListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .Returns(ToAsyncEnumerable(new List<Stripe.Subscription>()));

    _service = new StripeReconciliationService(
      configuration,
      _mockSubscriptionService.Object,
      _mockStripeSubscriptionService.Object,
      _mockCustomerService.Object,
      Mock.Of<ILogger<StripeReconciliationService>>());
  }

  private static async IAsyncEnumerable<Stripe.Subscription> ToAsyncEnumerable(IEnumerable<Stripe.Subscription> items)
  {
    foreach (var item in items)
    {
      yield return item;
    }

    await Task.CompletedTask;
  }

  private static UserSubscription CreateLocalSubscription(string stripeId = "sub_local", int id = 1) => new()
  {
    Id = id,
    UserId = 1,
    PlanId = 1,
    StripeSubscriptionId = stripeId,
    Status = SubscriptionStatus.Active
  };

  [Fact]
  public async Task ReconcileAsync_WithLocalSubscription_ShouldSyncFromStripe()
  {
    // Arrange
    var local = CreateLocalSubscription();
    var stripeSubscription = new Stripe.Subscription { Id = "sub_local", Status = "active" };

    _mockSubscriptionService.Setup(x => x.GetReconcilableSubscriptionsAsync())
      .ReturnsAsync(new List<UserSubscription> { local });
    _mockStripeSubscriptionService
      .Setup(x => x.GetAsync("sub_local", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(stripeSubscription);

    // Act
    var result = await _service.ReconcileAsync();

    // Assert
    _mockSubscriptionService.Verify(
      x => x.SyncFromStripeAsync(stripeSubscription, It.IsAny<Func<Task<int?>>?>()), Times.Once);
    Assert.Equal(1, result.LocalChecked);
    Assert.Equal(1, result.LocalUpdated);
    Assert.Equal(0, result.Errors);
  }

  [Fact]
  public async Task ReconcileAsync_WhenSubscriptionMissingOnStripe_ShouldCancelLocally()
  {
    // Arrange
    var local = CreateLocalSubscription();
    var missingException = new StripeException("No such subscription")
    {
      StripeError = new StripeError { Code = "resource_missing" }
    };

    _mockSubscriptionService.Setup(x => x.GetReconcilableSubscriptionsAsync())
      .ReturnsAsync(new List<UserSubscription> { local });
    _mockStripeSubscriptionService
      .Setup(x => x.GetAsync("sub_local", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(missingException);

    // Act
    var result = await _service.ReconcileAsync();

    // Assert
    _mockSubscriptionService.Verify(
      x => x.UpdateSubscriptionStatusAsync("sub_local", SubscriptionStatus.Canceled), Times.Once);
    _mockSubscriptionService.Verify(
      x => x.SyncFromStripeAsync(It.IsAny<Stripe.Subscription>(), It.IsAny<Func<Task<int?>>?>()), Times.Never);
    Assert.Equal(1, result.LocalUpdated);
    Assert.Equal(0, result.Errors);
  }

  [Fact]
  public async Task ReconcileAsync_WhenOneLocalSubscriptionFails_ShouldContinueWithOthers()
  {
    // Arrange
    var failing = CreateLocalSubscription("sub_fail", 1);
    var healthy = CreateLocalSubscription("sub_ok", 2);

    _mockSubscriptionService.Setup(x => x.GetReconcilableSubscriptionsAsync())
      .ReturnsAsync(new List<UserSubscription> { failing, healthy });
    _mockStripeSubscriptionService
      .Setup(x => x.GetAsync("sub_fail", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new HttpRequestException("network down"));
    _mockStripeSubscriptionService
      .Setup(x => x.GetAsync("sub_ok", It.IsAny<SubscriptionGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new Stripe.Subscription { Id = "sub_ok", Status = "active" });

    // Act
    var result = await _service.ReconcileAsync();

    // Assert
    _mockSubscriptionService.Verify(
      x => x.SyncFromStripeAsync(It.Is<Stripe.Subscription>(s => s.Id == "sub_ok"), It.IsAny<Func<Task<int?>>?>()),
      Times.Once);
    Assert.Equal(2, result.LocalChecked);
    Assert.Equal(1, result.LocalUpdated);
    Assert.Equal(1, result.Errors);
  }

  [Fact]
  public async Task ReconcileAsync_WithActiveStripeSubscriptionMissingLocally_ShouldCreateFromStripe()
  {
    // Arrange
    var orphan = new Stripe.Subscription { Id = "sub_orphan", CustomerId = "cus_1", Status = "active" };

    _mockStripeSubscriptionService
      .Setup(x => x.ListAutoPagingAsync(It.IsAny<SubscriptionListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .Returns(ToAsyncEnumerable(new[] { orphan }));
    _mockSubscriptionService.Setup(x => x.GetByStripeSubscriptionIdAsync("sub_orphan"))
      .ReturnsAsync((UserSubscription?)null);

    // Act
    var result = await _service.ReconcileAsync();

    // Assert
    _mockSubscriptionService.Verify(
      x => x.SyncFromStripeAsync(orphan, It.IsAny<Func<Task<int?>>?>()), Times.Once);
    Assert.Equal(1, result.MissingCreated);
    Assert.Equal(0, result.Errors);
  }

  [Fact]
  public async Task ReconcileAsync_WithActiveStripeSubscriptionKnownLocally_ShouldNotCreate()
  {
    // Arrange
    var known = new Stripe.Subscription { Id = "sub_known", Status = "active" };

    _mockStripeSubscriptionService
      .Setup(x => x.ListAutoPagingAsync(It.IsAny<SubscriptionListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .Returns(ToAsyncEnumerable(new[] { known }));
    _mockSubscriptionService.Setup(x => x.GetByStripeSubscriptionIdAsync("sub_known"))
      .ReturnsAsync(CreateLocalSubscription("sub_known"));

    // Act
    var result = await _service.ReconcileAsync();

    // Assert
    _mockSubscriptionService.Verify(
      x => x.SyncFromStripeAsync(It.IsAny<Stripe.Subscription>(), It.IsAny<Func<Task<int?>>?>()), Times.Never);
    Assert.Equal(0, result.MissingCreated);
  }

  [Fact]
  public async Task ReconcileAsync_WhenOrphanSyncFails_ShouldCountErrorAndContinue()
  {
    // Arrange - two orphans; the first cannot resolve a user, the second succeeds.
    var badOrphan = new Stripe.Subscription { Id = "sub_bad", CustomerId = "cus_bad", Status = "active" };
    var goodOrphan = new Stripe.Subscription { Id = "sub_good", CustomerId = "cus_good", Status = "active" };

    _mockStripeSubscriptionService
      .Setup(x => x.ListAutoPagingAsync(It.IsAny<SubscriptionListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .Returns(ToAsyncEnumerable(new[] { badOrphan, goodOrphan }));
    _mockSubscriptionService.Setup(x => x.GetByStripeSubscriptionIdAsync(It.IsAny<string>()))
      .ReturnsAsync((UserSubscription?)null);
    _mockSubscriptionService
      .Setup(x => x.SyncFromStripeAsync(badOrphan, It.IsAny<Func<Task<int?>>?>()))
      .ThrowsAsync(new InvalidOperationException("Could not determine user"));
    _mockSubscriptionService
      .Setup(x => x.SyncFromStripeAsync(goodOrphan, It.IsAny<Func<Task<int?>>?>()))
      .ReturnsAsync(CreateLocalSubscription("sub_good"));

    // Act
    var result = await _service.ReconcileAsync();

    // Assert
    Assert.Equal(1, result.MissingCreated);
    Assert.Equal(1, result.Errors);
  }

  [Fact]
  public async Task ReconcileAsync_OrphanUserIdFallback_ShouldReadCustomerMetadata()
  {
    // Arrange
    var orphan = new Stripe.Subscription { Id = "sub_orphan", CustomerId = "cus_42", Status = "active" };
    Func<Task<int?>>? capturedFallback = null;

    _mockStripeSubscriptionService
      .Setup(x => x.ListAutoPagingAsync(It.IsAny<SubscriptionListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .Returns(ToAsyncEnumerable(new[] { orphan }));
    _mockSubscriptionService.Setup(x => x.GetByStripeSubscriptionIdAsync("sub_orphan"))
      .ReturnsAsync((UserSubscription?)null);
    _mockSubscriptionService
      .Setup(x => x.SyncFromStripeAsync(orphan, It.IsAny<Func<Task<int?>>?>()))
      .Callback<Stripe.Subscription, Func<Task<int?>>?>((_, fallback) => capturedFallback = fallback)
      .ReturnsAsync(CreateLocalSubscription("sub_orphan"));
    _mockCustomerService
      .Setup(x => x.GetAsync("cus_42", It.IsAny<CustomerGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new Customer { Id = "cus_42", Metadata = new Dictionary<string, string> { ["userId"] = "42" } });

    // Act
    await _service.ReconcileAsync();

    // Assert
    Assert.NotNull(capturedFallback);
    Assert.Equal(42, await capturedFallback!());
  }
}
