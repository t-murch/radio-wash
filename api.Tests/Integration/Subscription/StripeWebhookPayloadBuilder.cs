using System.Text.Json;

namespace RadioWash.Api.Tests.Integration.Subscription;

/// <summary>
/// Builds Stripe webhook JSON payloads for integration tests.
/// Each method accepts an optional eventId parameter for idempotency testing.
/// </summary>
public static class StripeWebhookPayloadBuilder
{
    public static string CreateSubscriptionCreatedWebhook(
        string subscriptionId,
        string customerId,
        string priceId,
        int userId,
        string status = "active",
        string? eventId = null)
    {
        var payload = new
        {
            id = eventId ?? $"evt_{Guid.NewGuid():N}",
            @object = "event",
            api_version = "2020-08-27",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new
            {
                @object = new
                {
                    id = subscriptionId,
                    @object = "subscription",
                    customer = customerId,
                    status = status,
                    items = new
                    {
                        @object = "list",
                        data = new[]
                        {
                            new
                            {
                                id = $"si_{Guid.NewGuid():N}",
                                @object = "subscription_item",
                                price = new
                                {
                                    id = priceId,
                                    @object = "price"
                                }
                            }
                        }
                    },
                    metadata = new Dictionary<string, string>
                    {
                        { "userId", userId.ToString() }
                    }
                }
            },
            livemode = false,
            pending_webhooks = 1,
            request = new
            {
                id = $"req_{Guid.NewGuid():N}",
                idempotency_key = (string?)null
            },
            type = "customer.subscription.created"
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string CreateSubscriptionUpdatedWebhook(
        string subscriptionId,
        string status,
        DateTime? periodStart = null,
        DateTime? periodEnd = null,
        string? eventId = null)
    {
        var start = periodStart ?? DateTime.UtcNow.AddDays(-30);
        var end = periodEnd ?? DateTime.UtcNow.AddDays(30);

        var payload = new
        {
            id = eventId ?? $"evt_{Guid.NewGuid():N}",
            @object = "event",
            api_version = "2020-08-27",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new
            {
                @object = new
                {
                    id = subscriptionId,
                    @object = "subscription",
                    status = status,
                    items = new
                    {
                        @object = "list",
                        data = new[]
                        {
                            new
                            {
                                id = $"si_{Guid.NewGuid():N}",
                                @object = "subscription_item",
                                current_period_start = ((DateTimeOffset)start).ToUnixTimeSeconds(),
                                current_period_end = ((DateTimeOffset)end).ToUnixTimeSeconds()
                            }
                        }
                    }
                }
            },
            livemode = false,
            pending_webhooks = 1,
            request = new
            {
                id = $"req_{Guid.NewGuid():N}",
                idempotency_key = (string?)null
            },
            type = "customer.subscription.updated"
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string CreateSubscriptionDeletedWebhook(
        string subscriptionId,
        string? eventId = null)
    {
        var payload = new
        {
            id = eventId ?? $"evt_{Guid.NewGuid():N}",
            @object = "event",
            api_version = "2020-08-27",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new
            {
                @object = new
                {
                    id = subscriptionId,
                    @object = "subscription",
                    status = "canceled"
                }
            },
            livemode = false,
            pending_webhooks = 1,
            request = new
            {
                id = $"req_{Guid.NewGuid():N}",
                idempotency_key = (string?)null
            },
            type = "customer.subscription.deleted"
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string CreateInvoicePaymentSucceededWebhook(
        string invoiceId,
        string? subscriptionId = null,
        string? eventId = null)
    {
        var payload = new
        {
            id = eventId ?? $"evt_{Guid.NewGuid():N}",
            @object = "event",
            api_version = "2020-08-27",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new
            {
                @object = new
                {
                    id = invoiceId,
                    @object = "invoice",
                    subscription = subscriptionId
                }
            },
            livemode = false,
            pending_webhooks = 1,
            request = new
            {
                id = $"req_{Guid.NewGuid():N}",
                idempotency_key = (string?)null
            },
            type = "invoice.payment_succeeded"
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string CreateInvoicePaymentFailedWebhook(
        string invoiceId,
        string? subscriptionId = null,
        string? eventId = null)
    {
        var payload = new
        {
            id = eventId ?? $"evt_{Guid.NewGuid():N}",
            @object = "event",
            api_version = "2020-08-27",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new
            {
                @object = new
                {
                    id = invoiceId,
                    @object = "invoice",
                    subscription = subscriptionId
                }
            },
            livemode = false,
            pending_webhooks = 1,
            request = new
            {
                id = $"req_{Guid.NewGuid():N}",
                idempotency_key = (string?)null
            },
            type = "invoice.payment_failed"
        };

        return JsonSerializer.Serialize(payload);
    }
}
