using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Models.DTO;

public static class SubscriptionMappingExtensions
{
  public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan)
  {
    return new SubscriptionPlanDto
    {
      Id = plan.Id,
      Name = plan.Name,
      Price = plan.PriceInCents / 100m,
      BillingPeriod = plan.BillingPeriod,
      MaxPlaylists = plan.MaxPlaylists,
      MaxTracksPerPlaylist = plan.MaxTracksPerPlaylist,
      Features = ParseFeatures(plan.Features),
      IsActive = plan.IsActive
    };
  }

  public static UserSubscriptionDto ToDto(this UserSubscription subscription)
  {
    return new UserSubscriptionDto
    {
      Id = subscription.Id,
      Status = subscription.Status,
      CurrentPeriodStart = subscription.CurrentPeriodStart,
      CurrentPeriodEnd = subscription.CurrentPeriodEnd,
      CanceledAt = subscription.CanceledAt,
      Plan = subscription.Plan.ToDto(),
      CreatedAt = subscription.CreatedAt
    };
  }

  public static List<string> ParseFeatures(string? featuresJson, ILogger? logger = null)
  {
    try
    {
      if (string.IsNullOrEmpty(featuresJson) || featuresJson == "{}")
      {
        return new List<string>();
      }

      var features = JsonSerializer.Deserialize<List<string>>(featuresJson);
      return features ?? new List<string>();
    }
    catch (JsonException ex)
    {
      logger?.LogWarning(ex, "Failed to parse features JSON: {FeaturesJson}", featuresJson);
      return new List<string>();
    }
  }
}
