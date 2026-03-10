using System.Net;
using System.Net.Http.Json;
using RadioWash.Api.Tests.Integration.TestHelpers;
using Xunit.Abstractions;

namespace RadioWash.Api.Tests.Integration.Subscription;

/// <summary>
/// Integration tests for the checkout endpoint.
/// Outbound Stripe calls are REAL (test mode) — checkout creates a real Stripe session.
/// </summary>
public class CheckoutIntegrationTests : SubscriptionTestBase
{
    private readonly ITestOutputHelper _output;

    public CheckoutIntegrationTests(SubscriptionWebApplicationFactory factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    [Fact]
    public async Task Checkout_WithValidPlan_ReturnsCheckoutUrl()
    {
        // Arrange — requires real Stripe credentials for outbound API calls
        if (!HasRealStripeCredentials())
        {
            _output.WriteLine("SKIPPED: Real Stripe test-mode credentials not configured. Set Stripe:SecretKey to a sk_test_ value.");
            return;
        }

        var client = CreateAuthenticatedClient();
        var planId = await GetPlanIdAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/subscription/checkout", new { planId });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CheckoutResponse>();
        Assert.NotNull(body?.CheckoutUrl);
        Assert.StartsWith("https://checkout.stripe.com/", body.CheckoutUrl);
    }

    [Fact]
    public async Task Checkout_WithNonexistentPlan_Returns400()
    {
        // Arrange
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/subscription/checkout", new { planId = 99999 });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_Unauthenticated_Returns401()
    {
        // Arrange — no auth token
        var client = Factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/subscription/checkout", new { planId = 1 });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private record CheckoutResponse(string? CheckoutUrl);
}
