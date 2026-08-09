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
            // Must match the unit_amount of the Stripe price in Stripe:PricePlanId; checkout
            // bills from Stripe, so a mismatch here only misreports the price to the user.
            PriceInCents = 500, // $5.00/month
            BillingPeriod = "monthly",
            StripePriceId = stripePriceId,
            MaxPlaylists = 10, // enforced by SubscriptionService.EnforcePlanLimitAsync
            // No per-playlist track cap: nothing enforces one (CleanPlaylistService accepts any
            // size), so advertising a number here would be a designed limit that does not exist.
            MaxTracksPerPlaylist = null,
            Features = """["Daily automatic playlist synchronization", "Up to 10 sync configurations", "Manual sync triggering", "Sync history and status tracking", "Smart track matching and cleaning"]""",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.SubscriptionPlans.Add(syncPlan);
        await context.SaveChangesAsync();
    }
}
