using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Integration.TestHelpers;

namespace RadioWash.Api.Tests.Integration.Subscription;

/// <summary>
/// Integration tests for the Stripe webhook pipeline.
/// Full flow: real HTTP → real HandleWebhookAsync (signature bypassed) → real ProcessWebhookAsync → real database.
/// </summary>
public class WebhookIntegrationTests : SubscriptionTestBase
{
    public WebhookIntegrationTests(SubscriptionWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task SubscriptionCreated_CreatesRecordInDatabase()
    {
        // Arrange
        var stripeSubId = $"sub_{Guid.NewGuid():N}";
        var customerId = $"cus_{Guid.NewGuid():N}";
        var priceId = await GetStripePriceIdAsync();

        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(
            stripeSubId, customerId, priceId, TestUserId);

        // Act
        var response = await PostWebhookAsync(payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var subscription = await WithDbContextAsync(async db =>
            await db.UserSubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId));

        Assert.NotNull(subscription);
        Assert.Equal(TestUserId, subscription.UserId);
        Assert.Equal(stripeSubId, subscription.StripeSubscriptionId);
        Assert.Equal(customerId, subscription.StripeCustomerId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task MissingSignatureHeader_Returns400()
    {
        // Arrange
        var client = Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/subscription/webhook")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        // No Stripe-Signature header

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProcessingFailure_Returns200NotBadRequest()
    {
        // Arrange — use a nonexistent userId so processing fails
        var stripeSubId = $"sub_{Guid.NewGuid():N}";
        var priceId = await GetStripePriceIdAsync();

        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(
            stripeSubId, $"cus_{Guid.NewGuid():N}", priceId, userId: 99999);

        // Act
        var response = await PostWebhookAsync(payload);

        // Assert — controller returns 200 to prevent Stripe retries even when processing fails
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify no subscription was created for the fake user
        var subscription = await WithDbContextAsync(async db =>
            await db.UserSubscriptions
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId));

        Assert.Null(subscription);
    }

    [Fact]
    public async Task DuplicateEvent_IsIdempotent()
    {
        // Arrange
        var eventId = $"evt_{Guid.NewGuid():N}";
        var stripeSubId = $"sub_{Guid.NewGuid():N}";
        var customerId = $"cus_{Guid.NewGuid():N}";
        var priceId = await GetStripePriceIdAsync();

        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(
            stripeSubId, customerId, priceId, TestUserId, eventId: eventId);

        // Act — send the same event twice
        var response1 = await PostWebhookAsync(payload);
        var response2 = await PostWebhookAsync(payload);

        // Assert — both return 200
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // Only one subscription should exist
        var count = await WithDbContextAsync(async db =>
            await db.UserSubscriptions
                .CountAsync(s => s.StripeSubscriptionId == stripeSubId));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SubscriptionDeleted_SetsStatusToCanceled()
    {
        // Arrange — create subscription first
        var stripeSubId = $"sub_{Guid.NewGuid():N}";
        var subscription = await CreateSubscriptionViaWebhookAsync(stripeSubId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);

        var deletePayload = StripeWebhookPayloadBuilder.CreateSubscriptionDeletedWebhook(stripeSubId);

        // Act
        var response = await PostWebhookAsync(deletePayload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await WithDbContextAsync(async db =>
            await db.UserSubscriptions
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId));

        Assert.NotNull(updated);
        Assert.Equal(SubscriptionStatus.Canceled, updated.Status);
    }

    [Fact]
    public async Task SubscriptionUpdated_UpdatesStatusAndDates()
    {
        // Arrange — create subscription first
        var stripeSubId = $"sub_{Guid.NewGuid():N}";
        await CreateSubscriptionViaWebhookAsync(stripeSubId);

        var periodStart = DateTime.UtcNow.AddDays(-15);
        var periodEnd = DateTime.UtcNow.AddDays(15);
        var updatePayload = StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook(
            stripeSubId, "past_due", periodStart, periodEnd);

        // Act
        var response = await PostWebhookAsync(updatePayload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await WithDbContextAsync(async db =>
            await db.UserSubscriptions
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId));

        Assert.NotNull(updated);
        Assert.Equal(SubscriptionStatus.PastDue, updated.Status);
        // Period dates should be set (allow some tolerance for timestamp conversion)
        Assert.NotNull(updated.CurrentPeriodStart);
        Assert.NotNull(updated.CurrentPeriodEnd);
    }

    [Fact]
    public async Task InvoicePaymentSucceeded_SetsStatusToActive()
    {
        // Arrange — create subscription with incomplete status via direct DB insert
        var stripeSubId = $"sub_{Guid.NewGuid():N}";
        var planId = await GetPlanIdAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
            dbContext.UserSubscriptions.Add(new UserSubscription
            {
                UserId = TestUserId,
                PlanId = planId,
                StripeSubscriptionId = stripeSubId,
                StripeCustomerId = $"cus_{Guid.NewGuid():N}",
                Status = SubscriptionStatus.Incomplete,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var invoicePayload = StripeWebhookPayloadBuilder.CreateInvoicePaymentSucceededWebhook(
            $"in_{Guid.NewGuid():N}", stripeSubId);

        // Act
        var response = await PostWebhookAsync(invoicePayload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await WithDbContextAsync(async db =>
            await db.UserSubscriptions
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId));

        Assert.NotNull(updated);
        Assert.Equal(SubscriptionStatus.Active, updated.Status);
    }
}
