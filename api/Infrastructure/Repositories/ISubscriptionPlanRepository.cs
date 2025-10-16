using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface ISubscriptionPlanRepository
{
  Task<SubscriptionPlan?> GetByIdAsync(int planId);
  Task<SubscriptionPlan?> GetByNameAsync(string name);
  Task<SubscriptionPlan?> GetByStripePriceIdAsync(string stripePriceId);
  Task<IEnumerable<SubscriptionPlan>> GetActiveAsync();
  Task<SubscriptionPlan> CreateAsync(SubscriptionPlan plan);
  Task<SubscriptionPlan> UpdateAsync(SubscriptionPlan plan);
  Task SaveChangesAsync();
}
