using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Infrastructure.Repositories;

public class UserSubscriptionRepositoryTests : IDisposable
{
  private readonly RadioWashDbContext _context;
  private readonly UserSubscriptionRepository _repository;

  public UserSubscriptionRepositoryTests()
  {
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;

    _context = new RadioWashDbContext(options);
    _repository = new UserSubscriptionRepository(_context);
  }

  public void Dispose()
  {
    _context.Dispose();
  }

  [Fact]
  public async Task GetByIdAsync_WithExistingId_ShouldReturnSubscription()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var subscription = CreateUserSubscription(1);
    _context.UserSubscriptions.Add(subscription);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByIdAsync(subscription.Id);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(subscription.Id, result.Id);
    Assert.Equal(1, result.UserId);
  }

  [Fact]
  public async Task GetByIdAsync_WithNonExistentId_ShouldReturnNull()
  {
    // Act
    var result = await _repository.GetByIdAsync(999);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetByUserIdAsync_WithActiveSubscription_ShouldReturnSubscription()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var subscription = CreateUserSubscription(1, SubscriptionStatus.Active);
    _context.UserSubscriptions.Add(subscription);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByUserIdAsync(1);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(1, result.UserId);
    Assert.Equal(SubscriptionStatus.Active, result.Status);
  }

  [Fact]
  public async Task GetByUserIdAsync_WithCanceledSubscription_ShouldReturnCanceledSubscription()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var subscription = CreateUserSubscription(1, SubscriptionStatus.Canceled);
    _context.UserSubscriptions.Add(subscription);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByUserIdAsync(1);

    // Assert
    // Note: GetByUserIdAsync returns the most recent subscription regardless of status
    Assert.NotNull(result);
    Assert.Equal(1, result.UserId);
    Assert.Equal(SubscriptionStatus.Canceled, result.Status);
  }

  [Fact]
  public async Task GetByStripeSubscriptionIdAsync_WithExistingId_ShouldReturnSubscription()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var subscription = CreateUserSubscription(1);
    subscription.StripeSubscriptionId = "sub_test123";
    _context.UserSubscriptions.Add(subscription);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByStripeSubscriptionIdAsync("sub_test123");

    // Assert
    Assert.NotNull(result);
    Assert.Equal("sub_test123", result.StripeSubscriptionId);
    Assert.Equal(1, result.UserId);
  }

  [Fact]
  public async Task GetByStripeSubscriptionIdAsync_WithNonExistentId_ShouldReturnNull()
  {
    // Act
    var result = await _repository.GetByStripeSubscriptionIdAsync("sub_nonexistent");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task HasActiveSubscriptionAsync_WithActiveSubscription_ShouldReturnTrue()
  {
    // Arrange
    var subscription = CreateUserSubscription(1, SubscriptionStatus.Active);
    _context.UserSubscriptions.Add(subscription);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.HasActiveSubscriptionAsync(1);

    // Assert
    Assert.True(result);
  }

  [Fact]
  public async Task HasActiveSubscriptionAsync_WithoutActiveSubscription_ShouldReturnFalse()
  {
    // Arrange
    var subscription = CreateUserSubscription(1, SubscriptionStatus.Canceled);
    _context.UserSubscriptions.Add(subscription);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.HasActiveSubscriptionAsync(1);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task HasActiveSubscriptionAsync_WithNoSubscription_ShouldReturnFalse()
  {
    // Act
    var result = await _repository.HasActiveSubscriptionAsync(999);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task GetExpiringSubscriptionsAsync_ShouldReturnExpiredSubscriptions()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();

    // Add second user for testing
    var user2 = new User
    {
      Id = 2,
      SupabaseId = "test-uuid-2",
      DisplayName = "testuser2",
      Email = "test2@example.com",
      CreatedAt = DateTime.UtcNow
    };
    _context.Users.Add(user2);
    await _context.SaveChangesAsync();

    var expiredDate = DateTime.UtcNow.AddDays(-1);
    var futureDate = DateTime.UtcNow.AddDays(30);

    // Create expired subscription with ACTIVE status (the method filters by active status)
    var expiredSubscription = CreateUserSubscription(1, SubscriptionStatus.Active);
    expiredSubscription.CurrentPeriodEnd = expiredDate;

    // Create non-expired subscription
    var activeSubscription = CreateUserSubscription(2, SubscriptionStatus.Active);
    activeSubscription.CurrentPeriodEnd = futureDate;

    _context.UserSubscriptions.AddRange(expiredSubscription, activeSubscription);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetExpiringSubscriptionsAsync(DateTime.UtcNow);

    // Assert
    Assert.Single(result);
    Assert.Equal(1, result.First().UserId);
    Assert.True(result.First().CurrentPeriodEnd < DateTime.UtcNow);
  }

  [Fact]
  public async Task GetActiveSubscriptionsAsync_ShouldReturnOnlyActiveSubscriptions()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();

    // Add second user
    var user2 = new User
    {
      Id = 2,
      SupabaseId = "test-uuid-2",
      DisplayName = "testuser2",
      Email = "test2@example.com",
      CreatedAt = DateTime.UtcNow
    };
    _context.Users.Add(user2);
    await _context.SaveChangesAsync();

    var activeSubscription1 = CreateUserSubscription(1, SubscriptionStatus.Active);
    var activeSubscription2 = CreateUserSubscription(2, SubscriptionStatus.Active);
    var canceledSubscription = CreateUserSubscription(1, SubscriptionStatus.Canceled);

    _context.UserSubscriptions.AddRange(activeSubscription1, activeSubscription2, canceledSubscription);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetActiveSubscriptionsAsync();

    // Assert
    Assert.Equal(2, result.Count());
    Assert.All(result, s => Assert.Equal(SubscriptionStatus.Active, s.Status));
  }

  [Fact]
  public async Task CreateAsync_WithValidSubscription_ShouldCreateAndReturnSubscription()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var subscription = CreateUserSubscription(1);

    // Act
    var result = await _repository.CreateAsync(subscription);

    // Assert
    Assert.NotNull(result);
    Assert.True(result.Id > 0);
    Assert.Equal(1, result.UserId);

    var savedSubscription = await _context.UserSubscriptions.FindAsync(result.Id);
    Assert.NotNull(savedSubscription);
    Assert.Equal(1, savedSubscription.UserId);
  }

  [Fact]
  public async Task UpdateAsync_WithExistingSubscription_ShouldUpdateSubscription()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var subscription = CreateUserSubscription(1, SubscriptionStatus.Active);
    _context.UserSubscriptions.Add(subscription);
    await _context.SaveChangesAsync();

    subscription.Status = SubscriptionStatus.Canceled;
    subscription.CanceledAt = DateTime.UtcNow;

    // Act
    var result = await _repository.UpdateAsync(subscription);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(SubscriptionStatus.Canceled, result.Status);
    Assert.NotNull(result.CanceledAt);

    var updatedSubscription = await _context.UserSubscriptions.FindAsync(subscription.Id);
    Assert.Equal(SubscriptionStatus.Canceled, updatedSubscription!.Status);
    Assert.NotNull(updatedSubscription.CanceledAt);
  }

  [Fact]
  public async Task SaveChangesAsync_ShouldPersistChanges()
  {
    // Arrange
    await SeedRequiredEntitiesAsync();
    var subscription = CreateUserSubscription(1);
    _context.UserSubscriptions.Add(subscription);

    // Act
    await _repository.SaveChangesAsync();

    // Assert
    var savedSubscription = await _context.UserSubscriptions
        .FirstOrDefaultAsync(s => s.UserId == 1);
    Assert.NotNull(savedSubscription);
  }

  private async Task SeedRequiredEntitiesAsync()
  {
    // Add a user
    var user = new User
    {
      Id = 1,
      SupabaseId = "test-uuid-1",
      DisplayName = "testuser",
      Email = "test@example.com",
      CreatedAt = DateTime.UtcNow
    };
    _context.Users.Add(user);

    // Add a subscription plan
    var plan = new SubscriptionPlan
    {
      Id = 1,
      Name = "Test Plan",
      PriceInCents = 999,
      BillingPeriod = "monthly",
      IsActive = true,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _context.SubscriptionPlans.Add(plan);

    await _context.SaveChangesAsync();
  }

  private static UserSubscription CreateUserSubscription(int userId, string status = SubscriptionStatus.Active)
  {
    return new UserSubscription
    {
      UserId = userId,
      PlanId = 1,
      StripeSubscriptionId = $"sub_{userId}",
      StripeCustomerId = $"cus_{userId}",
      Status = status,
      CurrentPeriodStart = DateTime.UtcNow.AddDays(-30),
      CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }
}
