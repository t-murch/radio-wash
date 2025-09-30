namespace RadioWash.Api.Services.Interfaces;

public interface ISyncSchedulerService
{
  void InitializeScheduledJobs();
  Task ProcessScheduledSyncsAsync();
  Task ValidateSubscriptionsAsync();
  DateTime CalculateNextSyncTime(string frequency, DateTime? lastSync = null);
}
