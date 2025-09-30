using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class SyncTimeCalculator : ISyncTimeCalculator
{
  public DateTime CalculateNextSyncTime(string frequency, DateTime? lastSync = null)
  {
    var baseTime = lastSync ?? DateTime.UtcNow;

    return frequency.ToLowerInvariant() switch
    {
      "daily" => CalculateNextDailySync(baseTime),
      "weekly" => baseTime.AddDays(7),
      "monthly" => baseTime.AddMonths(1),
      _ => baseTime.AddDays(1) // Default to daily
    };
  }

  private static DateTime CalculateNextDailySync(DateTime baseTime)
  {
    // Schedule for 00:01 (12:01 AM) the next day
    var tomorrow = baseTime.Date.AddDays(1);
    return tomorrow.AddMinutes(1); // 00:01
  }
}