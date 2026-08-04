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
  public async Task UpdateSubscriptionStatusAsync_ActiveToCanceled_UpdatesStatusStampsCanceledAtAndDisablesConfigs()
  {
    // Arrange
    var userId = 1;
    var stripeSubscriptionId = "sub_123";
    var subscription = CreateUserSubscription(userId);
    subscription.StripeSubscriptionId = stripeSubscriptionId;
    var enabledConfig = new PlaylistSyncConfig { Id = 1, UserId = userId, IsActive = true };

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetEnabledByUserIdAsync(userId))
        .ReturnsAsync(new[] { enabledConfig });
    _mockUnitOfWork.Setup(x => x.SyncConfigs.DisableConfigAsync(It.IsAny<int>(), It.IsAny<string?>()))
        .Returns(Task.CompletedTask);

    // Act
    var result = await _subscriptionService.UpdateSubscriptionStatusAsync(stripeSubscriptionId, SubscriptionStatus.Canceled);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(SubscriptionStatus.Canceled, result.Status);
    Assert.NotNull(result.CanceledAt);

    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(It.Is<UserSubscription>(
        s => s.Status == SubscriptionStatus.Canceled)), Times.Once);
    // Transitioning out of active must disable running sync configs
    _mockUnitOfWork.Verify(
        x => x.SyncConfigs.DisableConfigAsync(enabledConfig.Id, AutoDisableReason.SubscriptionInactive),
        Times.Once);
  }

  [Fact]
  public async Task UpdateSubscriptionStatusAsync_ActiveToPastDue_DisablesEnabledConfigs()
  {
    // Arrange
    var userId = 2;
    var stripeSubscriptionId = "sub_past_due";
    var subscription = CreateUserSubscription(userId);
    subscription.StripeSubscriptionId = stripeSubscriptionId;
    var enabledConfig = new PlaylistSyncConfig { Id = 21, UserId = userId, IsActive = true };

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetEnabledByUserIdAsync(userId))
        .ReturnsAsync(new[] { enabledConfig });
    _mockUnitOfWork.Setup(x => x.SyncConfigs.DisableConfigAsync(It.IsAny<int>(), It.IsAny<string?>()))
        .Returns(Task.CompletedTask);

    // Act
    var result = await _subscriptionService.UpdateSubscriptionStatusAsync(stripeSubscriptionId, SubscriptionStatus.PastDue);

    // Assert
    Assert.Equal(SubscriptionStatus.PastDue, result.Status);
    Assert.Null(result.CanceledAt); // only canceled stamps CanceledAt
    _mockUnitOfWork.Verify(
        x => x.SyncConfigs.DisableConfigAsync(enabledConfig.Id, AutoDisableReason.SubscriptionInactive),
        Times.Once);
  }

  [Fact]
  public async Task UpdateSubscriptionStatusAsync_CanceledToActive_ReenablesAutoDisabledConfigs()
  {
    // Arrange
    var userId = 3;
    var stripeSubscriptionId = "sub_reactivated";
    var subscription = CreateUserSubscription(userId);
    subscription.StripeSubscriptionId = stripeSubscriptionId;
    subscription.Status = SubscriptionStatus.Canceled;
    var autoDisabled = new PlaylistSyncConfig
    {
      Id = 31,
      UserId = userId,
      IsActive = false,
      AutoDisabledReason = AutoDisableReason.SubscriptionInactive
    };

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetAutoDisabledByUserIdAsync(userId, AutoDisableReason.SubscriptionInactive))
        .ReturnsAsync(new[] { autoDisabled });
    _mockUnitOfWork.Setup(x => x.SyncConfigs.EnableConfigAsync(It.IsAny<int>()))
        .Returns(Task.CompletedTask);

    // Act
    var result = await _subscriptionService.UpdateSubscriptionStatusAsync(stripeSubscriptionId, SubscriptionStatus.Active);

    // Assert
    Assert.Equal(SubscriptionStatus.Active, result.Status);
    _mockUnitOfWork.Verify(x => x.SyncConfigs.EnableConfigAsync(autoDisabled.Id), Times.Once);
    _mockUnitOfWork.Verify(x => x.SyncConfigs.DisableConfigAsync(It.IsAny<int>(), It.IsAny<string?>()), Times.Never);
  }

  [Fact]
  public async Task UpdateSubscriptionStatusAsync_ActiveToActive_DoesNotTouchConfigs()
  {
    // Arrange - no entitlement transition means no config side effects
    var stripeSubscriptionId = "sub_no_transition";
    var subscription = CreateUserSubscription(4);
    subscription.StripeSubscriptionId = stripeSubscriptionId;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);

    // Act
    await _subscriptionService.UpdateSubscriptionStatusAsync(stripeSubscriptionId, SubscriptionStatus.Active);

    // Assert
    _mockUnitOfWork.Verify(x => x.SyncConfigs.GetEnabledByUserIdAsync(It.IsAny<int>()), Times.Never);
    _mockUnitOfWork.Verify(x => x.SyncConfigs.GetAutoDisabledByUserIdAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task UpdateSubscriptionStatusAsync_WithIncompleteExpired_MapsToCanceled()
  {
    // Arrange
    var userId = 5;
    var stripeSubscriptionId = "sub_incomplete_expired";
    var subscription = CreateUserSubscription(userId);
    subscription.StripeSubscriptionId = stripeSubscriptionId;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetEnabledByUserIdAsync(userId))
        .ReturnsAsync(Array.Empty<PlaylistSyncConfig>());

    // Act
    var result = await _subscriptionService.UpdateSubscriptionStatusAsync(stripeSubscriptionId, "incomplete_expired");

    // Assert - Stripe's incomplete_expired collapses into the local canceled status
    Assert.Equal(SubscriptionStatus.Canceled, result.Status);
    Assert.NotNull(result.CanceledAt);
  }

  [Fact]
  public async Task UpdateSubscriptionStatusAsync_WithExistingCanceledAt_PreservesOriginalTimestamp()
  {
    // Arrange
    var userId = 6;
    var stripeSubscriptionId = "sub_already_canceled";
    var originalCanceledAt = DateTime.UtcNow.AddDays(-3);
    var subscription = CreateUserSubscription(userId);
    subscription.StripeSubscriptionId = stripeSubscriptionId;
    subscription.Status = SubscriptionStatus.PastDue;
    subscription.CanceledAt = originalCanceledAt;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync(stripeSubscriptionId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);

    // Act
    var result = await _subscriptionService.UpdateSubscriptionStatusAsync(stripeSubscriptionId, SubscriptionStatus.Canceled);

    // Assert - CanceledAt is only stamped when null
    Assert.Equal(SubscriptionStatus.Canceled, result.Status);
    Assert.Equal(originalCanceledAt, result.CanceledAt);
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

  #region SyncFromStripeAsync

  [Fact]
  public async Task SyncFromStripeAsync_WithExistingLocalRow_UpdatesStatusAndDatesWithoutCreating()
  {
    // Arrange
    var userId = 1;
    var periodStart = DateTime.UtcNow.AddDays(-1);
    var periodEnd = DateTime.UtcNow.AddDays(29);
    var existing = CreateUserSubscription(userId);
    existing.Status = SubscriptionStatus.Trialing;

    var stripeSubscription = CreateStripeSubscription(
        "sub_123", "cus_123", SubscriptionStatus.Active, "price_x", periodStart, periodEnd);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync("sub_123"))
        .ReturnsAsync(existing);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);

    // Act
    var result = await _subscriptionService.SyncFromStripeAsync(stripeSubscription);

    // Assert
    Assert.Equal(SubscriptionStatus.Active, result.Status);
    Assert.Equal(periodStart, result.CurrentPeriodStart);
    Assert.Equal(periodEnd, result.CurrentPeriodEnd);

    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(existing), Times.Once);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()), Times.Never);
  }

  [Fact]
  public async Task SyncFromStripeAsync_WithNoLocalRowAndMetadataUserId_CreatesSubscription()
  {
    // Arrange - out-of-order `updated` event arriving before `created` must still create the row
    var userId = 42;
    var planId = 7;
    var periodStart = DateTime.UtcNow;
    var periodEnd = DateTime.UtcNow.AddDays(30);

    var stripeSubscription = CreateStripeSubscription(
        "sub_new", "cus_new", SubscriptionStatus.Active, "price_x", periodStart, periodEnd, userId);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync("sub_new"))
        .ReturnsAsync((UserSubscription?)null);
    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetByStripePriceIdAsync("price_x"))
        .ReturnsAsync(CreateSubscriptionPlan(planId, "Pro"));
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(false);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => { s.Id = 99; return s; });
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetAutoDisabledByUserIdAsync(userId, AutoDisableReason.SubscriptionInactive))
        .ReturnsAsync(Array.Empty<PlaylistSyncConfig>());

    // Act
    var result = await _subscriptionService.SyncFromStripeAsync(stripeSubscription);

    // Assert
    Assert.Equal(userId, result.UserId);
    Assert.Equal(planId, result.PlanId);
    Assert.Equal(SubscriptionStatus.Active, result.Status);

    _mockUnitOfWork.Verify(x => x.UserSubscriptions.TryCreateAsync(It.Is<UserSubscription>(
        s => s.UserId == userId &&
             s.PlanId == planId &&
             s.StripeSubscriptionId == "sub_new" &&
             s.StripeCustomerId == "cus_new" &&
             s.Status == SubscriptionStatus.Active &&
             s.CurrentPeriodStart == periodStart &&
             s.CurrentPeriodEnd == periodEnd)), Times.Once);
  }

  [Fact]
  public async Task SyncFromStripeAsync_WhenCreateRaceIsLost_FallsBackToUpdatingWinnersRow()
  {
    // Arrange - the webhook, checkout/complete, and reconciliation can race to create the
    // same brand-new subscription. The loser's TryCreateAsync returns null (unique index);
    // the rerun must find the winner's row and take the update path.
    var userId = 42;
    var stripeSubscription = CreateStripeSubscription(
        "sub_race", "cus_race", SubscriptionStatus.Active, "price_x",
        DateTime.UtcNow, DateTime.UtcNow.AddDays(30), userId);

    var winnersRow = CreateUserSubscription(userId);
    winnersRow.StripeSubscriptionId = "sub_race";

    _mockUnitOfWork.SetupSequence(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync("sub_race"))
        .ReturnsAsync((UserSubscription?)null)
        .ReturnsAsync(winnersRow);
    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetByStripePriceIdAsync("price_x"))
        .ReturnsAsync(CreateSubscriptionPlan(7, "Pro"));
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(false);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription?)null);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);

    // Act
    var result = await _subscriptionService.SyncFromStripeAsync(stripeSubscription);

    // Assert - exactly one create attempt, then the winner's row was updated instead
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()), Times.Once);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(winnersRow), Times.Once);
    Assert.Equal(winnersRow.Id, result.Id);
  }

  [Fact]
  public async Task SyncFromStripeAsync_WithNoMetadataUserId_UsesFallbackResolver()
  {
    // Arrange
    var fallbackUserId = 55;
    var stripeSubscription = CreateStripeSubscription(
        "sub_fallback", "cus_fallback", SubscriptionStatus.Active, "price_x",
        DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync("sub_fallback"))
        .ReturnsAsync((UserSubscription?)null);
    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetByStripePriceIdAsync("price_x"))
        .ReturnsAsync(CreateSubscriptionPlan(1, "Pro"));
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(fallbackUserId))
        .ReturnsAsync(false);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetAutoDisabledByUserIdAsync(fallbackUserId, AutoDisableReason.SubscriptionInactive))
        .ReturnsAsync(Array.Empty<PlaylistSyncConfig>());

    // Act
    var result = await _subscriptionService.SyncFromStripeAsync(
        stripeSubscription, () => Task.FromResult<int?>(fallbackUserId));

    // Assert
    Assert.Equal(fallbackUserId, result.UserId);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.TryCreateAsync(It.Is<UserSubscription>(
        s => s.UserId == fallbackUserId)), Times.Once);
  }

  [Fact]
  public async Task SyncFromStripeAsync_WithUnresolvableUser_Throws()
  {
    // Arrange - no metadata userId and the fallback resolver comes up empty
    var stripeSubscription = CreateStripeSubscription(
        "sub_no_user", "cus_no_user", SubscriptionStatus.Active, "price_x",
        DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync("sub_no_user"))
        .ReturnsAsync((UserSubscription?)null);
    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetByStripePriceIdAsync("price_x"))
        .ReturnsAsync(CreateSubscriptionPlan(1, "Pro"));

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.SyncFromStripeAsync(
            stripeSubscription, () => Task.FromResult<int?>(null)));

    Assert.Contains("Could not determine user", exception.Message);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()), Times.Never);
  }

  [Fact]
  public async Task SyncFromStripeAsync_NewSubscriptionWithoutItems_Throws()
  {
    // Arrange - no items means no price, so no local plan can be resolved
    var stripeSubscription = new Stripe.Subscription
    {
      Id = "sub_no_items",
      CustomerId = "cus_no_items",
      Status = SubscriptionStatus.Active
    };

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync("sub_no_items"))
        .ReturnsAsync((UserSubscription?)null);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.SyncFromStripeAsync(stripeSubscription));

    Assert.Contains("no items with a price", exception.Message);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()), Times.Never);
  }

  [Fact]
  public async Task SyncFromStripeAsync_WithUnknownPlanPrice_Throws()
  {
    // Arrange
    var stripeSubscription = CreateStripeSubscription(
        "sub_unknown_price", "cus_x", SubscriptionStatus.Active, "price_unknown",
        DateTime.UtcNow, DateTime.UtcNow.AddDays(30), userId: 1);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync("sub_unknown_price"))
        .ReturnsAsync((UserSubscription?)null);
    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetByStripePriceIdAsync("price_unknown"))
        .ReturnsAsync((SubscriptionPlan?)null);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.SyncFromStripeAsync(stripeSubscription));

    Assert.Contains("No local plan matches Stripe price", exception.Message);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()), Times.Never);
  }

  [Fact]
  public async Task SyncFromStripeAsync_WithIncompleteExpired_StoresCanceled()
  {
    // Arrange
    var userId = 8;
    var stripeSubscription = CreateStripeSubscription(
        "sub_expired", "cus_expired", "incomplete_expired", "price_x",
        DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, userId);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync("sub_expired"))
        .ReturnsAsync((UserSubscription?)null);
    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetByStripePriceIdAsync("price_x"))
        .ReturnsAsync(CreateSubscriptionPlan(1, "Pro"));
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(false);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetEnabledByUserIdAsync(userId))
        .ReturnsAsync(Array.Empty<PlaylistSyncConfig>());

    // Act
    var result = await _subscriptionService.SyncFromStripeAsync(stripeSubscription);

    // Assert - incomplete_expired collapses into the local canceled status
    Assert.Equal(SubscriptionStatus.Canceled, result.Status);
    Assert.NotNull(result.CanceledAt);
  }

  [Fact]
  public async Task SyncFromStripeAsync_WhenUserAlreadyHasActiveSubscription_StillCreates()
  {
    // Arrange - the user paid on Stripe's side; a conflicting local subscription is logged,
    // not a reason to drop the record
    var userId = 9;
    var stripeSubscription = CreateStripeSubscription(
        "sub_conflict", "cus_conflict", SubscriptionStatus.Active, "price_x",
        DateTime.UtcNow, DateTime.UtcNow.AddDays(30), userId);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByStripeSubscriptionIdAsync("sub_conflict"))
        .ReturnsAsync((UserSubscription?)null);
    _mockUnitOfWork.Setup(x => x.SubscriptionPlans.GetByStripePriceIdAsync("price_x"))
        .ReturnsAsync(CreateSubscriptionPlan(1, "Pro"));
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.HasActiveSubscriptionAsync(userId))
        .ReturnsAsync(true);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockUnitOfWork.Setup(x => x.SyncConfigs.GetAutoDisabledByUserIdAsync(userId, AutoDisableReason.SubscriptionInactive))
        .ReturnsAsync(Array.Empty<PlaylistSyncConfig>());

    // Act
    var result = await _subscriptionService.SyncFromStripeAsync(stripeSubscription);

    // Assert - created despite the conflict, no throw
    Assert.Equal(userId, result.UserId);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.TryCreateAsync(It.IsAny<UserSubscription>()), Times.Once);
  }

  #endregion

  private static Stripe.Subscription CreateStripeSubscription(
      string subscriptionId,
      string customerId,
      string status,
      string priceId,
      DateTime periodStart,
      DateTime periodEnd,
      int? userId = null)
  {
    var subscription = new Stripe.Subscription
    {
      Id = subscriptionId,
      CustomerId = customerId,
      Status = status,
      Items = new Stripe.StripeList<Stripe.SubscriptionItem>
      {
        Data = new List<Stripe.SubscriptionItem>
        {
          new Stripe.SubscriptionItem
          {
            Price = new Stripe.Price { Id = priceId },
            CurrentPeriodStart = periodStart,
            CurrentPeriodEnd = periodEnd
          }
        }
      }
    };

    if (userId.HasValue)
    {
      subscription.Metadata = new Dictionary<string, string> { { "userId", userId.Value.ToString() } };
    }

    return subscription;
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
