using Hangfire;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Hangfire job orchestrator
/// </summary>
public class HangfireJobOrchestrator : IJobOrchestrator
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfireJobOrchestrator(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    public Task<string> EnqueueJobAsync(int jobId)
    {
        var hangfireId = _backgroundJobClient.Enqueue<ICleanPlaylistJobProcessor>(
            processor => processor.ProcessJobAsync(jobId));
        return Task.FromResult(hangfireId);
    }

    public Task CancelJobAsync(string hangfireJobId)
    {
        _backgroundJobClient.Delete(hangfireJobId);
        return Task.CompletedTask;
    }
}