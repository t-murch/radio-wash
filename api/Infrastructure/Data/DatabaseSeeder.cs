using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Data;

public static class DatabaseSeeder
{
    public static async Task SeedSubscriptionPlansAsync(RadioWashDbContext context, IConfiguration configuration)
    {
        // Check if subscription plans already exist
        if (await context.SubscriptionPlans.AnyAsync())
        {
            return; // Already seeded
        }

        var stripePriceId = configuration["Stripe:PricePlanId"];

        if (string.IsNullOrEmpty(stripePriceId))
        {
            return; // No Stripe configuration
        }

        var syncPlan = new SubscriptionPlan
        {
            Name = "Sync Plan",
            PriceInCents = 299, // $5.00/month
            BillingPeriod = "monthly",
            StripePriceId = stripePriceId,
            MaxPlaylists = 10, // 10 playlists maximum
            MaxTracksPerPlaylist = 200, // 200 tracks per playlist maximum
            Features = """["Daily automatic playlist synchronization", "Up to 10 sync configurations", "Up to 200 tracks per playlist", "Manual sync triggering", "Sync history and status tracking", "Smart track matching and cleaning"]""",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.SubscriptionPlans.Add(syncPlan);
        await context.SaveChangesAsync();
    }
}
