using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Integration.TestHelpers;

namespace RadioWash.Api.Tests.Integration.Subscription;

/// <summary>
/// Base class for subscription integration tests.
/// Creates a test user via GoTrue, waits for the auth trigger to create the DB record,
/// and provides helpers for authenticated HTTP requests and webhook posting.
/// </summary>
public abstract class SubscriptionTestBase : IClassFixture<SubscriptionWebApplicationFactory>, IAsyncLifetime
{
    protected readonly SubscriptionWebApplicationFactory Factory;
    private string? _testUserEmail;
    private string? _testUserToken;
    private string? _testUserSupabaseId;
    protected int TestUserId { get; private set; }

    protected SubscriptionTestBase(SubscriptionWebApplicationFactory factory)
    {
        Factory = factory;
    }

    /// <summary>
    /// Returns true if real Stripe test-mode credentials are configured.
    /// Tests that make outbound Stripe API calls should skip when this is false.
    /// </summary>
    protected bool HasRealStripeCredentials()
    {
        using var scope = Factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var secretKey = config["Stripe:SecretKey"];
        return !string.IsNullOrEmpty(secretKey) && secretKey.StartsWith("sk_test_") && secretKey != "sk_test_fake";
    }

    public async Task InitializeAsync()
    {
        // Ensure EF migrations and seed data are applied before any test runs
        await Factory.EnsureMigratedAsync();

        _testUserEmail = $"sub-test-{Guid.NewGuid():N}@example.com";
        var password = "TestPassword123!";

        var authResponse = await Factory.CreateTestUserAsync(_testUserEmail, password);
        _testUserToken = authResponse.access_token;
        _testUserSupabaseId = authResponse.user?.id;

        // Wait for auth trigger to create user in DB
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        var user = await WaitForUserCreationAsync(dbContext, _testUserSupabaseId!, TimeSpan.FromSeconds(5));
        TestUserId = user.Id;
    }

    public async Task DisposeAsync()
    {
        // Clean up subscription-related records for this test user
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

        var subscriptions = await dbContext.UserSubscriptions
            .Where(s => s.UserId == TestUserId)
            .ToListAsync();

        if (subscriptions.Any())
        {
            dbContext.UserSubscriptions.RemoveRange(subscriptions);
            await dbContext.SaveChangesAsync();
        }

        // Clean up webhook event and retry records created during tests.
        // Use ExecuteDeleteAsync to avoid optimistic concurrency issues
        // when multiple test classes clean up in parallel.
        await dbContext.ProcessedWebhookEvents.ExecuteDeleteAsync();
        await dbContext.WebhookRetries.ExecuteDeleteAsync();

        // Delete GoTrue user
        if (_testUserSupabaseId != null)
        {
            await Factory.DeleteTestUserAsync(_testUserSupabaseId);
        }
    }

    /// <summary>
    /// Creates an HttpClient with the test user's Bearer token.
    /// </summary>
    protected HttpClient CreateAuthenticatedClient()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _testUserToken);
        return client;
    }

    /// <summary>
    /// POSTs a webhook payload to /api/subscription/webhook with the Stripe-Signature header.
    /// </summary>
    protected async Task<HttpResponseMessage> PostWebhookAsync(string payload, string? signature = "test")
    {
        var client = Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/subscription/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        if (signature != null)
        {
            request.Headers.Add("Stripe-Signature", signature);
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Gets the seeded plan ID from the database.
    /// </summary>
    protected async Task<int> GetPlanIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        var plan = await dbContext.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.IsActive);
        return plan?.Id ?? throw new InvalidOperationException("No active subscription plan found. Has the database been seeded?");
    }

    /// <summary>
    /// Gets the Stripe price ID from the seeded plan.
    /// </summary>
    protected async Task<string> GetStripePriceIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        var plan = await dbContext.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.IsActive && p.StripePriceId != null);
        return plan?.StripePriceId ?? throw new InvalidOperationException("No active plan with StripePriceId found.");
    }

    /// <summary>
    /// Creates a subscription record in the DB via a customer.subscription.created webhook.
    /// Returns the created UserSubscription.
    /// </summary>
    protected async Task<UserSubscription> CreateSubscriptionViaWebhookAsync(
        string? stripeSubscriptionId = null,
        string? stripeCustomerId = null,
        string status = "active")
    {
        stripeSubscriptionId ??= $"sub_{Guid.NewGuid():N}";
        stripeCustomerId ??= $"cus_{Guid.NewGuid():N}";

        var priceId = await GetStripePriceIdAsync();
        var payload = StripeWebhookPayloadBuilder.CreateSubscriptionCreatedWebhook(
            stripeSubscriptionId, stripeCustomerId, priceId, TestUserId, status);

        var response = await PostWebhookAsync(payload);
        response.EnsureSuccessStatusCode();

        // Retrieve the created subscription
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        var subscription = await dbContext.UserSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId);

        return subscription ?? throw new InvalidOperationException(
            $"Subscription with StripeSubscriptionId {stripeSubscriptionId} was not created by the webhook.");
    }

    /// <summary>
    /// Provides a scoped RadioWashDbContext for assertions.
    /// </summary>
    protected async Task<T> WithDbContextAsync<T>(Func<RadioWashDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        return await action(dbContext);
    }

    /// <summary>
    /// Cleans up ProcessedWebhookEvent records for a specific event ID.
    /// Useful when tests need to re-process the same event.
    /// </summary>
    protected async Task CleanupWebhookEventAsync(string eventId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        var events = await dbContext.ProcessedWebhookEvents
            .Where(e => e.EventId == eventId)
            .ToListAsync();
        dbContext.ProcessedWebhookEvents.RemoveRange(events);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<User> WaitForUserCreationAsync(
        RadioWashDbContext dbContext, string supabaseId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.SupabaseId == supabaseId);
            if (user != null) return user;
            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"User with SupabaseId {supabaseId} was not created by the auth trigger within {timeout}.");
    }
}
