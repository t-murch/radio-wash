using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Infrastructure.Repositories;

public class SubscriptionPlanRepositoryTests : IDisposable
{
  private readonly RadioWashDbContext _context;
  private readonly SubscriptionPlanRepository _repository;

  public SubscriptionPlanRepositoryTests()
  {
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;

    _context = new RadioWashDbContext(options);
    _repository = new SubscriptionPlanRepository(_context);
  }

  public void Dispose()
  {
    _context.Dispose();
  }

  [Fact]
  public async Task GetByIdAsync_WithExistingId_ShouldReturnPlan()
  {
    // Arrange
    var plan = CreateSubscriptionPlan("Basic Plan");
    _context.SubscriptionPlans.Add(plan);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByIdAsync(plan.Id);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(plan.Id, result.Id);
    Assert.Equal("Basic Plan", result.Name);
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
  public async Task GetActiveAsync_ShouldReturnOnlyActivePlans()
  {
    // Arrange
    var activePlan = CreateSubscriptionPlan("Active Plan", true);
    var inactivePlan = CreateSubscriptionPlan("Inactive Plan", false);

    _context.SubscriptionPlans.AddRange(activePlan, inactivePlan);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetActiveAsync();

    // Assert
    Assert.Single(result);
    Assert.Equal("Active Plan", result.First().Name);
    Assert.True(result.First().IsActive);
  }

  [Fact]
  public async Task CreateAsync_WithValidPlan_ShouldCreateAndReturnPlan()
  {
    // Arrange
    var plan = CreateSubscriptionPlan("New Plan");

    // Act
    var result = await _repository.CreateAsync(plan);

    // Assert
    Assert.NotNull(result);
    Assert.True(result.Id > 0);
    Assert.Equal("New Plan", result.Name);

    var savedPlan = await _context.SubscriptionPlans.FindAsync(result.Id);
    Assert.NotNull(savedPlan);
    Assert.Equal("New Plan", savedPlan.Name);
  }

  [Fact]
  public async Task UpdateAsync_WithExistingPlan_ShouldUpdatePlan()
  {
    // Arrange
    var plan = CreateSubscriptionPlan("Original Name");
    _context.SubscriptionPlans.Add(plan);
    await _context.SaveChangesAsync();

    plan.Name = "Updated Name";
    plan.PriceInCents = 1999;

    // Act
    var result = await _repository.UpdateAsync(plan);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("Updated Name", result.Name);
    Assert.Equal(1999, result.PriceInCents);

    var updatedPlan = await _context.SubscriptionPlans.FindAsync(plan.Id);
    Assert.Equal("Updated Name", updatedPlan!.Name);
    Assert.Equal(1999, updatedPlan.PriceInCents);
  }

  [Fact]
  public async Task GetByNameAsync_WithExistingName_ShouldReturnPlan()
  {
    // Arrange
    var plan = CreateSubscriptionPlan("Unique Plan Name");
    _context.SubscriptionPlans.Add(plan);
    await _context.SaveChangesAsync();

    // Act
    var result = await _repository.GetByNameAsync("Unique Plan Name");

    // Assert
    Assert.NotNull(result);
    Assert.Equal("Unique Plan Name", result.Name);
    Assert.Equal(plan.Id, result.Id);
  }

  [Fact]
  public async Task GetByNameAsync_WithNonExistentName_ShouldReturnNull()
  {
    // Act
    var result = await _repository.GetByNameAsync("Non-existent Plan");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task SaveChangesAsync_ShouldPersistChanges()
  {
    // Arrange
    var plan = CreateSubscriptionPlan("Test Plan");
    await _repository.CreateAsync(plan);

    // Act
    await _repository.SaveChangesAsync();

    // Assert
    var savedPlan = await _context.SubscriptionPlans
        .FirstOrDefaultAsync(p => p.Name == "Test Plan");
    Assert.NotNull(savedPlan);
  }

  private static SubscriptionPlan CreateSubscriptionPlan(string name, bool isActive = true)
  {
    return new SubscriptionPlan
    {
      Name = name,
      PriceInCents = 999,
      BillingPeriod = "monthly",
      IsActive = isActive,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }
}
