using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class SubscriptionPlanRepository : ISubscriptionPlanRepository
{
    private readonly RadioWashDbContext _dbContext;

    public SubscriptionPlanRepository(RadioWashDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SubscriptionPlan?> GetByIdAsync(int planId)
    {
        return await _dbContext.SubscriptionPlans
            .FirstOrDefaultAsync(sp => sp.Id == planId);
    }

    public async Task<SubscriptionPlan?> GetByNameAsync(string name)
    {
        return await _dbContext.SubscriptionPlans
            .FirstOrDefaultAsync(sp => sp.Name == name);
    }

    public async Task<IEnumerable<SubscriptionPlan>> GetActiveAsync()
    {
        return await _dbContext.SubscriptionPlans
            .Where(sp => sp.IsActive)
            .OrderBy(sp => sp.PriceInCents)
            .ToListAsync();
    }

    public async Task<SubscriptionPlan> CreateAsync(SubscriptionPlan plan)
    {
        _dbContext.SubscriptionPlans.Add(plan);
        await _dbContext.SaveChangesAsync();
        return plan;
    }

    public async Task<SubscriptionPlan> UpdateAsync(SubscriptionPlan plan)
    {
        plan.UpdatedAt = DateTime.UtcNow;
        _dbContext.SubscriptionPlans.Update(plan);
        await _dbContext.SaveChangesAsync();
        return plan;
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}