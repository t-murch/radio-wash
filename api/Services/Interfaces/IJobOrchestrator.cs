namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Abstraction for job orchestration
/// </summary>
public interface IJobOrchestrator
{
  Task<string> EnqueueJobAsync(int jobId);
  Task CancelJobAsync(string hangfireJobId);
}
