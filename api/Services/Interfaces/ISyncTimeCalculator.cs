namespace RadioWash.Api.Services.Interfaces;

public interface ISyncTimeCalculator
{
  DateTime CalculateNextSyncTime(string frequency, DateTime? lastSync = null);
}
