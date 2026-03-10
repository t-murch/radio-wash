using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Integration.TestHelpers;

namespace RadioWash.Api.Tests.Integration.Subscription;

/// <summary>
/// Integration tests for subscription status, plans, current, and verify-session endpoints.
/// </summary>
public class SubscriptionStatusIntegrationTests : SubscriptionTestBase
{
    public SubscriptionStatusIntegrationTests(SubscriptionWebApplicationFactory factory) : base(factory) { }

    #region Plans

    [Fact]
    public async Task Plans_ReturnsSeededPlan()
    {
        // Arrange
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/subscription/plans");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();

        // Verify plan details
        Assert.Contains("Sync Plan", content);

        // Parse and check structure
        var plans = JsonSerializer.Deserialize<JsonElement[]>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(plans);
        Assert.NotEmpty(plans);

        var plan = plans[0];
        Assert.Equal("Sync Plan", plan.GetProperty("name").GetString());
        Assert.True(plan.GetProperty("isActive").GetBoolean());

        // StripePriceId should NOT be exposed in the DTO
        Assert.False(plan.TryGetProperty("stripePriceId", out _));
    }

    #endregion

    #region Status

    [Fact]
    public async Task Status_NoSubscription_ReturnsFalse()
    {
        // Arrange
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/subscription/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(body);
        Assert.False(body.HasActiveSubscription);
    }

    [Fact]
    public async Task Status_WithActiveSubscription_ReturnsTrue()
    {
        // Arrange — create subscription via webhook
        await CreateSubscriptionViaWebhookAsync();
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/subscription/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(body);
        Assert.True(body.HasActiveSubscription);
    }

    [Fact]
    public async Task Status_WithCancelAtPeriodEnd_StillReturnsTrue()
    {
        // Arrange — create subscription then update to cancel_at_period_end
        var stripeSubId = $"sub_{Guid.NewGuid():N}";
        await CreateSubscriptionViaWebhookAsync(stripeSubId);

        // Update status via webhook
        var periodEnd = DateTime.UtcNow.AddDays(30);
        var updatePayload = StripeWebhookPayloadBuilder.CreateSubscriptionUpdatedWebhook(
            stripeSubId, "cancel_at_period_end", DateTime.UtcNow.AddDays(-1), periodEnd);
        await PostWebhookAsync(updatePayload);

        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/subscription/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(body);
        Assert.True(body.HasActiveSubscription);
    }

    #endregion

    #region Current

    [Fact]
    public async Task Current_NoSubscription_ReturnsNoContent()
    {
        // Arrange
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/subscription/current");

        // Assert — Ok(null) returns 204 NoContent in ASP.NET
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Current_WithSubscription_ReturnsDetails()
    {
        // Arrange
        await CreateSubscriptionViaWebhookAsync();
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/subscription/current");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEqual("null", content);

        var subscription = JsonSerializer.Deserialize<JsonElement>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal("active", subscription.GetProperty("status").GetString());
        Assert.True(subscription.TryGetProperty("plan", out var plan));
        Assert.Equal("Sync Plan", plan.GetProperty("name").GetString());
    }

    #endregion

    #region Verify Session

    [Fact]
    public async Task VerifySession_MissingSessionId_Returns400()
    {
        // Arrange
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/subscription/verify-session");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifySession_NonexistentSession_ReturnsFalse()
    {
        // Arrange
        var client = CreateAuthenticatedClient();

        // Act — real Stripe call with a fake session ID
        var response = await client.GetAsync("/api/subscription/verify-session?sessionId=cs_test_fake_nonexistent");

        // Assert — should handle gracefully
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VerifySessionResponse>();
        Assert.NotNull(body);
        Assert.False(body.Verified);
    }

    #endregion

    private record StatusResponse(bool HasActiveSubscription);
    private record VerifySessionResponse(bool Verified);
}
