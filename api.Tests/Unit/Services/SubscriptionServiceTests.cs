using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class SubscriptionServiceTests
{
  private readonly Mock<IUnitOfWork> _mockUnitOfWork;
  private readonly Mock<IStripeSubscriptionClient> _mockStripeSubscriptionClient;
  private readonly Mock<ILogger<SubscriptionService>> _mockLogger;
  private readonly SubscriptionService _subscriptionService;

  public SubscriptionServiceTests()
  {
    _mockUnitOfWork = new Mock<IUnitOfWork>();
    _mockStripeSubscriptionClient = new Mock<IStripeSubscriptionClient>();
    _mockLogger = new Mock<ILogger<SubscriptionService>>();

    _subscriptionService = new SubscriptionService(
        _mockUnitOfWork.Object,
        _mockStripeSubscriptionClient.Object,
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

  #region CancelSubscriptionAsync Tests

  [Fact]
  public async Task CancelSubscriptionAsync_ShouldCallStripeCancelAtPeriodEnd()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    SetupCancelMocks(subscription);

    // Act
    await _subscriptionService.CancelSubscriptionAsync(userId);

    // Assert
    _mockStripeSubscriptionClient.Verify(
        x => x.CancelAtPeriodEndAsync("sub_123"), Times.Once);
  }

  [Fact]
  public async Task CancelSubscriptionAsync_ShouldSetStatusToCancelAtPeriodEnd()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    SetupCancelMocks(subscription);

    // Act
    var result = await _subscriptionService.CancelSubscriptionAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(SubscriptionStatus.CancelAtPeriodEnd, result.Status);
    Assert.NotNull(result.CanceledAt);
  }

  [Fact]
  public async Task CancelSubscriptionAsync_ShouldNotDisableSyncConfigs()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    SetupCancelMocks(subscription);

    // Act
    await _subscriptionService.CancelSubscriptionAsync(userId);

    // Assert — sync configs should NOT be disabled (user keeps access until period end)
    _mockUnitOfWork.Verify(x => x.SyncConfigs.GetByUserIdAsync(It.IsAny<int>()), Times.Never);
    _mockUnitOfWork.Verify(x => x.SyncConfigs.DisableConfigAsync(It.IsAny<int>()), Times.Never);
  }

  [Fact]
  public async Task CancelSubscriptionAsync_ShouldUseTransaction()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    SetupCancelMocks(subscription);

    // Act
    await _subscriptionService.CancelSubscriptionAsync(userId);

    // Assert
    _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Never);
  }

  [Fact]
  public async Task CancelSubscriptionAsync_WhenStripeFails_ShouldRollback()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.BeginTransactionAsync())
        .Returns(Task.CompletedTask);
    _mockUnitOfWork.Setup(x => x.RollbackTransactionAsync())
        .Returns(Task.CompletedTask);

    _mockStripeSubscriptionClient.Setup(x => x.CancelAtPeriodEndAsync("sub_123"))
        .ThrowsAsync(new Stripe.StripeException("Stripe API error"));

    // Act & Assert
    await Assert.ThrowsAsync<Stripe.StripeException>(
        () => _subscriptionService.CancelSubscriptionAsync(userId));

    _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
    // No local DB update should have been persisted
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()), Times.Never);
  }

  [Fact]
  public async Task CancelSubscriptionAsync_WithNoSubscription_ShouldThrow()
  {
    // Arrange
    var userId = 1;
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync((UserSubscription?)null);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.CancelSubscriptionAsync(userId));

    Assert.Contains("No active subscription found", exception.Message);
    _mockStripeSubscriptionClient.Verify(
        x => x.CancelAtPeriodEndAsync(It.IsAny<string>()), Times.Never);
  }

  #endregion

  #region ResumeSubscriptionAsync Tests

  [Fact]
  public async Task ResumeSubscriptionAsync_ShouldCallStripeResumeAtPeriodEnd()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    subscription.Status = SubscriptionStatus.CancelAtPeriodEnd;
    SetupResumeMocks(subscription);

    // Act
    await _subscriptionService.ResumeSubscriptionAsync(userId);

    // Assert
    _mockStripeSubscriptionClient.Verify(
        x => x.ResumeAtPeriodEndAsync("sub_123"), Times.Once);
  }

  [Fact]
  public async Task ResumeSubscriptionAsync_ShouldSetStatusToActive()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    subscription.Status = SubscriptionStatus.CancelAtPeriodEnd;
    subscription.CanceledAt = DateTime.UtcNow;
    SetupResumeMocks(subscription);

    // Act
    var result = await _subscriptionService.ResumeSubscriptionAsync(userId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(SubscriptionStatus.Active, result.Status);
    Assert.Null(result.CanceledAt);
  }

  [Fact]
  public async Task ResumeSubscriptionAsync_ShouldUseTransaction()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    subscription.Status = SubscriptionStatus.CancelAtPeriodEnd;
    SetupResumeMocks(subscription);

    // Act
    await _subscriptionService.ResumeSubscriptionAsync(userId);

    // Assert
    _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Never);
  }

  [Fact]
  public async Task ResumeSubscriptionAsync_WhenStripeFails_ShouldRollback()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    subscription.Status = SubscriptionStatus.CancelAtPeriodEnd;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.BeginTransactionAsync())
        .Returns(Task.CompletedTask);
    _mockUnitOfWork.Setup(x => x.RollbackTransactionAsync())
        .Returns(Task.CompletedTask);

    _mockStripeSubscriptionClient.Setup(x => x.ResumeAtPeriodEndAsync("sub_123"))
        .ThrowsAsync(new Stripe.StripeException("Stripe API error"));

    // Act & Assert
    await Assert.ThrowsAsync<Stripe.StripeException>(
        () => _subscriptionService.ResumeSubscriptionAsync(userId));

    _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
    _mockUnitOfWork.Verify(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()), Times.Never);
  }

  [Fact]
  public async Task ResumeSubscriptionAsync_WithNoSubscription_ShouldThrow()
  {
    // Arrange
    var userId = 1;
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync((UserSubscription?)null);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.ResumeSubscriptionAsync(userId));

    Assert.Contains("No subscription found", exception.Message);
    _mockStripeSubscriptionClient.Verify(
        x => x.ResumeAtPeriodEndAsync(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task ResumeSubscriptionAsync_WithFullyCanceledSubscription_ShouldThrow()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    subscription.Status = SubscriptionStatus.Canceled;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.ResumeSubscriptionAsync(userId));

    Assert.Contains("fully canceled", exception.Message);
    _mockStripeSubscriptionClient.Verify(
        x => x.ResumeAtPeriodEndAsync(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task ResumeSubscriptionAsync_WithActiveSubscription_ShouldThrow()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    subscription.Status = SubscriptionStatus.Active;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.ResumeSubscriptionAsync(userId));

    Assert.Contains("not pending cancellation", exception.Message);
    _mockStripeSubscriptionClient.Verify(
        x => x.ResumeAtPeriodEndAsync(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task ResumeSubscriptionAsync_WithNoStripeSubscriptionId_ShouldThrow()
  {
    // Arrange
    var userId = 1;
    var subscription = CreateUserSubscription(userId);
    subscription.Status = SubscriptionStatus.CancelAtPeriodEnd;
    subscription.StripeSubscriptionId = null;

    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(userId))
        .ReturnsAsync(subscription);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _subscriptionService.ResumeSubscriptionAsync(userId));

    Assert.Contains("No Stripe subscription ID found", exception.Message);
    _mockStripeSubscriptionClient.Verify(
        x => x.ResumeAtPeriodEndAsync(It.IsAny<string>()), Times.Never);
  }

  #endregion

  #region Helper Methods

  private void SetupResumeMocks(UserSubscription subscription)
  {
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(subscription.UserId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.BeginTransactionAsync())
        .Returns(Task.CompletedTask);
    _mockUnitOfWork.Setup(x => x.CommitTransactionAsync())
        .Returns(Task.CompletedTask);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockStripeSubscriptionClient.Setup(x => x.ResumeAtPeriodEndAsync(subscription.StripeSubscriptionId!))
        .ReturnsAsync(new Stripe.Subscription { Id = subscription.StripeSubscriptionId, CancelAtPeriodEnd = false });
  }

  private void SetupCancelMocks(UserSubscription subscription)
  {
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.GetByUserIdAsync(subscription.UserId))
        .ReturnsAsync(subscription);
    _mockUnitOfWork.Setup(x => x.BeginTransactionAsync())
        .Returns(Task.CompletedTask);
    _mockUnitOfWork.Setup(x => x.CommitTransactionAsync())
        .Returns(Task.CompletedTask);
    _mockUnitOfWork.Setup(x => x.UserSubscriptions.UpdateAsync(It.IsAny<UserSubscription>()))
        .ReturnsAsync((UserSubscription s) => s);
    _mockStripeSubscriptionClient.Setup(x => x.CancelAtPeriodEndAsync(subscription.StripeSubscriptionId!))
        .ReturnsAsync(new Stripe.Subscription { Id = subscription.StripeSubscriptionId, CancelAtPeriodEnd = true });
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

  #endregion
}
