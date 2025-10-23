using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class SyncTimeCalculator : ISyncTimeCalculator
{
  public DateTime CalculateNextSyncTime(string frequency, DateTime? lastSync = null)
  {
    var baseTime = lastSync ?? DateTime.UtcNow;

    return frequency switch
    {
      SyncFrequency.Daily => GetNextDailySync(baseTime),
      SyncFrequency.Weekly => baseTime.AddDays(7),
      SyncFrequency.Manual => DateTime.MaxValue,
      _ => GetNextDailySync(baseTime)
    };
  }

  private static DateTime GetNextDailySync(DateTime baseTime)
  {
    // Schedule for 00:01 (12:01 AM) the next day
    var nextDay = baseTime.Date.AddDays(1);
    return nextDay.AddMinutes(1); // 00:01
  }
}
