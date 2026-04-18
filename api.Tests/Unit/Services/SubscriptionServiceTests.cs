using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Exceptions;
using RadioWash.Api.Services.Implementations;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class SubscriptionServiceTests
{
  private readonly Mock<IUnitOfWork> _mockUnitOfWork;
  private readonly Mock<ILogger<SubscriptionService>> _mockLogger;
  private readonly SubscriptionService _subscriptionService;

  public SubscriptionServiceTests()
  {
    _mockUnitOfWork = new Mock<IUnitOfWork>();
    _mockLogger = new Mock<ILogger<SubscriptionService>>();

    _subscriptionService = new SubscriptionService(
        _mockUnitOfWork.Object,
        _mockLogger.Object
    );
  }

  [Fact]
  public async Task GetActiveSubscriptionAsync_WithExistingSubscription_ShouldReturnSubscription()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);

    // Act
    var result = await _subscriptionService.GetActiveSubscriptionAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(userId, result.UserId);
    Assert.Equal(SubscriptionStatus.Active, result.Status);
  }

  [Fact]
  public async Task GetActiveSubscriptionAsync_WithNoSubscription_ShouldReturnNull()
  {
    // Arrange
    var userId = 1;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync((UserSubscription?)null);

    // Act
    var result = await _subscriptionService.GetActiveSubscriptionAsync(userId);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task HasActiveSubscriptionAsync_WithActiveSubscription_ShouldReturnTrue()
  {
    // Arrange
    var userId = 1;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(true);

    // Act
    var result = await _subscriptionService.HasActiveSubscriptionAsync(userId);

    // Assert
    Assert.True(result);
  }

  [Fact]
  public async Task HasActiveSubscriptionAsync_WithoutActiveSubscription_ShouldReturnFalse()
  {
    // Arrange
    var userId = 1;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(false);

    // Act
    var result = await _subscriptionService.HasActiveSubscriptionAsync(userId);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task CreateSubscriptionAsync_WithValidData_ShouldCreateSubscription()
  {
    // Arrange
    var userId = 1;
    var planId = 1;
    var stripeSubscriptionId = "sub_123";
    var stripeCustomerId = "cus_123";

    // Mock validation check - user doesn't have active subscription
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(false);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.CreateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => { s.Id = 1; return s; });

    // Act
    var result = await _subscriptionService.CreateSubscriptionAsync(userId, planId, stripeSubscriptionId, stripeCustomerId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(userId, result.UserId);
    Assert.Equal(planId, result.PlanId);
    Assert.Equal(stripeSubscriptionId, result.StripeSubscriptionId);
    Assert.Equal(stripeCustomerId, result.StripeCustomerId);
    Assert.Equal(SubscriptionStatus.Active, result.Status);

    _mockUnitOfWork.Verify(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId), Times.Once);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.CreateAsync(It.Is<UserSubscription>(
        s => s.UserId == userId &&
             s.PlanId == planId &&
             s.StripeSubscriptionId == stripeSubscriptionId &&
             s.StripeCustomerId == stripeCustomerId &&
             s.Status == SubscriptionStatus.Active)), Times.Once);
  }

  [Fact]
  public async Task CreateSubscriptionAsync_WithExistingActiveSubscription_ShouldThrowException()
  {
    // Arrange
    var userId = 1;
    var planId = 1;
    var stripeSubscriptionId = "sub_123";
    var stripeCustomerId = "cus_123";

    // Mock validation check - user already has active subscription
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(true);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.CreateSubscriptionAsync(userId, planId, stripeSubscriptionId, stripeCustomerId));

    Assert.Equal($"User {userId} already has an active subscription", exception.Message);

    // Verify that validation was called but creation was not
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId), Times.Once);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.CreateAsync(It.IsAny<UserSubscription>()), Times.Never);
  }

  [Fact]
  public async Task CreateSubscriptionAsync_ValidationCheckThrows_ShouldPropagateException()
  {
    // Arrange
    var userId = 1;
    var planId = 1;
    var stripeSubscriptionId = "sub_123";
    var stripeCustomerId = "cus_123";

    // Mock validation check to throw exception (e.g., database error)
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId))
        .ThrowsAsync(new InvalidOperationException("Database connection failed"));

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.CreateSubscriptionAsync(userId, planId, stripeSubscriptionId, stripeCustomerId));

    Assert.Equal("Database connection failed", exception.Message);

    // Verify that validation was called but creation was not
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId), Times.Once);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.CreateAsync(It.IsAny<UserSubscription>()), Times.Never);
  }

  [Fact]
  public async Task UpdateSubscriptionStatusAsync_WithValidSubscription_ShouldUpdateStatus()
  {
    // Arrange
    var stripeSubscriptionId = "sub_123";
    var newStatus = SubscriptionStatus.Canceled;
    var subscription = CreateUserSubscription(1);
    subscription.StripeSubscriptionId = stripeSubscriptionId;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);

    // Act
    var result = await _subscriptionService.UpdateSubscriptionStatusAsync(stripeSubscriptionId, newStatus);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(newStatus, result.Status);

    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(It.Is<UserSubscription>(
        s => s.Status == newStatus)), Times.Once);
  }

  [Fact]
  public async Task UpdateSubscriptionStatusAsync_WithNonExistentSubscription_ShouldThrowException()
  {
    // Arrange
    var stripeSubscriptionId = "sub_nonexistent";
    var newStatus = SubscriptionStatus.Canceled;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId))
        .ReturnsAsync((UserSubscription?)null);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.UpdateSubscriptionStatusAsync(stripeSubscriptionId, newStatus));

    Assert.Contains("Subscription with Stripe ID", exception.Message);
  }

  [Fact]
  public async Task GetAvailablePlansAsync_ShouldReturnActivePlans()
  {
    // Arrange
    var plans = new List<SubscriptionPlan>
        {
            CreateSubscriptionPlan(1, "Basic"),
            CreateSubscriptionPlan(2, "Premium")
        };

    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetActiveAsync())
        .ReturnsAsync(plans);

    // Act
    var result = await _subscriptionService.GetAvailablePlansAsync();

    // Assert
    Assert.NotNull(result);
    Assert.Equal(2, result.Count());
    Assert.Contains(result, p => p.Name == "Basic");
    Assert.Contains(result, p => p.Name == "Premium");
  }

  [Fact]
  public async Task GetPlanByIdAsync_WithValidId_ShouldReturnPlan()
  {
    // Arrange
    var planId = 1;
    var plan = CreateSubscriptionPlan(planId, "Basic");

    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetByIdAsync(planId))
        .ReturnsAsync(plan);

    // Act
    var result = await _subscriptionService.GetPlanByIdAsync(planId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(planId, result.Id);
    Assert.Equal("Basic", result.Name);
  }

  [Fact]
  public async Task GetPlanByIdAsync_WithInvalidId_ShouldReturnNull()
  {
    // Arrange
    var planId = 999;

    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetByIdAsync(planId))
        .ReturnsAsync((SubscriptionPlan?)null);

    // Act
    var result = await _subscriptionService.GetPlanByIdAsync(planId);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task CancelSubscriptionAsync_WithValidUser_ShouldCancelAndDisableSyncs()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    var syncConfigs = new List<PlaylistSyncConfig>
        {
            new PlaylistSyncConfig { Id = 1, UserId = userId, IsActive = true }
        };

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetEnabledByUserIdAsync(userId))
        .ReturnsAsync(syncConfigs);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.DisableConfigAsync(It.IsAny<int>(), It.IsAny<string?>()))
        .Returns(Task.CompletedTask);

    // Act
    var result = await _subscriptionService.CancelSubscriptionAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(SubscriptionStatus.Canceled, result.Status);
    Assert.NotNull(result.CanceledAt);

    _mockUnitOfWork.Verify(x => x.SyncConfigs.DisableConfigAsync(1, AutoDisableReason.SubscriptionInactive), Times.Once);
  }

  [Fact]
  public async Task ValidateSubscriptionsAsync_WithSubscriptionPastGraceWindow_TransitionsToCanceledAndDisablesEnabledConfigs()
  {
    // Arrange: subscription expired 48h ago (past the 24h grace window)
    var userId = 42;
    var expired = CreateUserSubscription(userId);
    expired.CurrentPeriodEnd = DateTime.UtcNow.AddHours(-48);

    var enabledConfig = new PlaylistSyncConfig { Id = 7, UserId = userId, IsActive = true };

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetExpiredActiveSubscriptionsAsync(It.IsAny<DateTime>()))
        .ReturnsAsync(new[] { expired });
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetEnabledByUserIdAsync(userId))
        .ReturnsAsync(new[] { enabledConfig });
    _mockUnitOfWork.Setup(x => x.SyncConfigs.DisableConfigAsync(It.IsAny<int>(), It.IsAny<string?>()))
        .Returns(Task.CompletedTask);

    // Act
    await _subscriptionService.ValidateSubscriptionsAsync();

    // Assert: subscription transitioned to Canceled, CanceledAt stamped, config tagged and disabled
    Assert.Equal(SubscriptionStatus.Canceled, expired.Status);
    Assert.NotNull(expired.CanceledAt);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(expired), Times.Once);
    _mockUnitOfWork.Verify(
        x => x.SyncConfigs.DisableConfigAsync(enabledConfig.Id, AutoDisableReason.SubscriptionInactive),
        Times.Once);
  }

  [Fact]
  public async Task ValidateSubscriptionsAsync_WithSubscriptionInsideGraceWindow_LeavesStateUnchanged()
  {
    // Arrange: the repository filter (GetExpiredActiveSubscriptionsAsync) is what enforces the
    // grace window — verify the service passes the correct cutoff (UtcNow - 24h). If nothing
    // matches the cutoff, nothing is updated.
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetExpiredActiveSubscriptionsAsync(It.IsAny<DateTime>()))
        .ReturnsAsync(Array.Empty<UserSubscription>());

    // Act
    await _subscriptionService.ValidateSubscriptionsAsync();

    // Assert: no writes happened, and the cutoff was at least 23 hours in the past (allows for
    // clock skew between capture and the assertion)
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()), Times.Never);
    _mockUnitOfWork.Verify(x => x.SyncConfigs.DisableConfigAsync(It.IsAny<int>(), It.IsAny<string?>()), Times.Never);
    _mockUnitOfWork.Verify(
        x => x.UserSubscriptions.GetExpiredActiveSubscriptionsAsync(It.Is<DateTime>(d => d < DateTime.UtcNow.AddHours(-23))),
        Times.Once);
  }

  [Fact]
  public async Task ValidateSubscriptionsAsync_WithOneFailingSubscription_ContinuesBatch()
  {
    // Arrange: two expired subscriptions; the first UpdateAsync throws
    var failing = CreateUserSubscription(1);
    failing.Id = 1;
    failing.CurrentPeriodEnd = DateTime.UtcNow.AddHours(-48);

    var succeeding = CreateUserSubscription(2);
    succeeding.Id = 2;
    succeeding.CurrentPeriodEnd = DateTime.UtcNow.AddHours(-48);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetExpiredActiveSubscriptionsAsync(It.IsAny<DateTime>()))
        .ReturnsAsync(new[] { failing, succeeding });

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(failing))
        .ThrowsAsync(new InvalidOperationException("db transient"));
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(succeeding))
        .ReturnsAsync(succeeding);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetEnabledByUserIdAsync(It.IsAny<int>()))
        .ReturnsAsync(Array.Empty<PlaylistSyncConfig>());

    // Act
    await _subscriptionService.ValidateSubscriptionsAsync();

    // Assert: both updates attempted; the second succeeded
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(failing), Times.Once);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(succeeding), Times.Once);
    Assert.Equal(SubscriptionStatus.Canceled, succeeding.Status);
  }

  [Fact]
  public async Task ReactivateSyncConfigsAsync_EnablesOnlyAutoDisabledConfigs()
  {
    // Arrange
    var userId = 5;
    var autoDisabled = new[]
    {
        new PlaylistSyncConfig { Id = 10, UserId = userId, IsActive = false, AutoDisabledReason = AutoDisableReason.SubscriptionInactive },
        new PlaylistSyncConfig { Id = 11, UserId = userId, IsActive = false, AutoDisabledReason = AutoDisableReason.SubscriptionInactive },
    };

    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetAutoDisabledByUserIdAsync(userId, AutoDisableReason.SubscriptionInactive))
        .ReturnsAsync(autoDisabled);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.EnableConfigAsync(It.IsAny<int>()))
        .Returns(Task.CompletedTask);

    // Act
    await _subscriptionService.ReactivateSyncConfigsAsync(userId);

    // Assert
    _mockUnitOfWork.Verify(x => x.SyncConfigs.EnableConfigAsync(10), Times.Once);
    _mockUnitOfWork.Verify(x => x.SyncConfigs.EnableConfigAsync(11), Times.Once);
  }

  [Fact]
  public async Task ReactivateSyncConfigsAsync_WithNoAutoDisabledConfigs_IsNoop()
  {
    // Arrange
    var userId = 6;
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetAutoDisabledByUserIdAsync(userId, AutoDisableReason.SubscriptionInactive))
        .ReturnsAsync(Array.Empty<PlaylistSyncConfig>());

    // Act
    await _subscriptionService.ReactivateSyncConfigsAsync(userId);

    // Assert
    _mockUnitOfWork.Verify(x => x.SyncConfigs.EnableConfigAsync(It.IsAny<int>()), Times.Never);
  }

  [Fact]
  public async Task EnforcePlanLimitAsync_AtLimit_Throws()
  {
    // Arrange
    var userId = 10;
    var subscription = CreateUserSubscription(userId);
    subscription.Plan = CreateSubscriptionPlanWithLimit(1, "Pro", maxPlaylists: 3);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.CountEnabledByUserIdAsync(userId))
        .ReturnsAsync(3);

    // Act + Assert
    var ex = await Assert.ThrowsAsync<PlanLimitExceededException>(
        () => _subscriptionService.EnforcePlanLimitAsync(userId));
    Assert.Equal("playlists", ex.LimitType);
    Assert.Equal(3, ex.Limit);
    Assert.Equal(3, ex.Current);
  }

  [Fact]
  public async Task EnforcePlanLimitAsync_BelowLimit_DoesNotThrow()
  {
    // Arrange
    var userId = 11;
    var subscription = CreateUserSubscription(userId);
    subscription.Plan = CreateSubscriptionPlanWithLimit(1, "Pro", maxPlaylists: 3);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.CountEnabledByUserIdAsync(userId))
        .ReturnsAsync(2);

    // Act
    await _subscriptionService.EnforcePlanLimitAsync(userId);

    // Assert: no exception means success; also confirm the count was queried
    _mockUnitOfWork.Verify(x => x.SyncConfigs.CountEnabledByUserIdAsync(userId), Times.Once);
  }

  [Fact]
  public async Task EnforcePlanLimitAsync_UnlimitedPlan_DoesNotQueryCount()
  {
    // Arrange: MaxPlaylists = null means unlimited
    var userId = 12;
    var subscription = CreateUserSubscription(userId);
    subscription.Plan = CreateSubscriptionPlanWithLimit(1, "Unlimited", maxPlaylists: null);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);

    // Act
    await _subscriptionService.EnforcePlanLimitAsync(userId);

    // Assert: count query should not fire when the plan is unlimited
    _mockUnitOfWork.Verify(x => x.SyncConfigs.CountEnabledByUserIdAsync(It.IsAny<int>()), Times.Never);
  }

  [Fact]
  public async Task EnforcePlanLimitAsync_NoSubscription_DoesNotThrow()
  {
    // Arrange: gate is HasActiveSubscriptionAsync (caller's responsibility) — EnforcePlanLimit
    // intentionally no-ops when there's no subscription so the two error paths stay distinct
    // at the HTTP boundary. Without this behavior the test for "at limit" can't exist as
    // written because the code would throw for a different reason.
    var userId = 13;
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync((UserSubscription?)null);

    // Act + Assert: no exception
    await _subscriptionService.EnforcePlanLimitAsync(userId);
    _mockUnitOfWork.Verify(x => x.SyncConfigs.CountEnabledByUserIdAsync(It.IsAny<int>()), Times.Never);
  }

  private static SubscriptionPlan CreateSubscriptionPlanWithLimit(int id, string name, int? maxPlaylists)
  {
    return new SubscriptionPlan
    {
      Id = id,
      Name = name,
      PriceInCents = 299,
      BillingPeriod = "monthly",
      IsActive = true,
      MaxPlaylists = maxPlaylists,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  private static UserSubscription CreateUserSubscription(int userId)
  {
    return new UserSubscription
    {
      Id = 1,
      UserId = userId,
      PlanId = 1,
      StripeSubscriptionId = "sub_123",
      StripeCustomerId = "cus_123",
      Status = SubscriptionStatus.Active,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
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
}
