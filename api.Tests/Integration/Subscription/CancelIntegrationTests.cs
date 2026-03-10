using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Integration.TestHelpers;

namespace RadioWash.Api.Tests.Integration.Subscription;

/// <summary>
/// Integration tests for subscription cancellation.
/// Cancel calls real Stripe API (test mode). Tests verify error handling
/// when Stripe subscription doesn't exist (validates rollback behavior).
/// </summary>
public class CancelIntegrationTests : SubscriptionTestBase
{
    public CancelIntegrationTests(SubscriptionWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Cancel_WithNoSubscription_ReturnsError()
    {
        // Arrange — user has no subscription
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsync("/api/subscription/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_WhenStripeSubscriptionNotFound_ReturnsError()
    {
        // Arrange — create subscription in DB with a fake stripeSubscriptionId
        var fakeStripeSubId = $"sub_fake_{Guid.NewGuid():N}";
        var planId = await GetPlanIdAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
            dbContext.UserSubscriptions.Add(new UserSubscription
            {
                UserId = TestUserId,
                PlanId = planId,
                StripeSubscriptionId = fakeStripeSubId,
                StripeCustomerId = $"cus_fake_{Guid.NewGuid():N}",
                Status = SubscriptionStatus.Active,
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var client = CreateAuthenticatedClient();

        // Act — real Stripe call will fail because subscription doesn't exist
        var response = await client.PostAsync("/api/subscription/cancel", null);

        // Assert — should return error
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // DB status should still be active (transaction rolled back)
        var subscription = await WithDbContextAsync(async db =>
            await db.UserSubscriptions
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == fakeStripeSubId));

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }
}
