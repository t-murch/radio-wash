using System.Text.Json;

namespace RadioWash.Api.Tests.Unit.Services;

public static class StripeWebhookPayloadBuilder
{
  public static string CreateSubscriptionCreatedWebhook(
      string subscriptionId, 
      string customerId, 
      string priceId, 
      int userId,
      string status = "active")
  {
    var payload = new
    {
      id = "evt_123",
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
                id = "si_123",
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
        id = "req_123",
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
      DateTime? periodEnd = null)
  {
    var start = periodStart ?? DateTime.UtcNow.AddDays(-30);
    var end = periodEnd ?? DateTime.UtcNow.AddDays(30);

    var payload = new
    {
      id = "evt_123",
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
                id = "si_123",
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
        id = "req_123",
        idempotency_key = (string?)null
      },
      type = "customer.subscription.updated"
    };

    return JsonSerializer.Serialize(payload);
  }

  public static string CreateSubscriptionDeletedWebhook(string subscriptionId)
  {
    var payload = new
    {
      id = "evt_123",
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
        id = "req_123",
        idempotency_key = (string?)null
      },
      type = "customer.subscription.deleted"
    };

    return JsonSerializer.Serialize(payload);
  }

  public static string CreateInvoicePaymentFailedWebhook(string invoiceId, string? subscriptionId = null)
  {
    var payload = new
    {
      id = "evt_123",
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
        id = "req_123",
        idempotency_key = (string?)null
      },
      type = "invoice.payment_failed"
    };

    return JsonSerializer.Serialize(payload);
  }

  public static string CreateInvoicePaymentSucceededWebhook(string invoiceId, string? subscriptionId = null)
  {
    var payload = new
    {
      id = "evt_123",
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
        id = "req_123",
        idempotency_key = (string?)null
      },
      type = "invoice.payment_succeeded"
    };

    return JsonSerializer.Serialize(payload);
  }

  public static string CreateCheckoutSessionCompletedWebhook(string sessionId, int userId)
  {
    var payload = new
    {
      id = "evt_123",
      @object = "event",
      api_version = "2020-08-27",
      created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
      data = new
      {
        @object = new
        {
          id = sessionId,
          @object = "checkout.session",
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
        id = "req_123",
        idempotency_key = (string?)null
      },
      type = "checkout.session.completed"
    };

    return JsonSerializer.Serialize(payload);
  }

  public static string CreateSubscriptionCreatedWebhookNoItems(string subscriptionId, string customerId)
  {
    var payload = new
    {
      id = "evt_123",
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
          status = "active",
          items = (object?)null // No items
        }
      },
      livemode = false,
      pending_webhooks = 1,
      request = new
      {
        id = "req_123",
        idempotency_key = (string?)null
      },
      type = "customer.subscription.created"
    };

    return JsonSerializer.Serialize(payload);
  }
}