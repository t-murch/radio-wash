using Hangfire;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Production dispatcher: hands delivery to Hangfire, whose default AutomaticRetry (10
/// attempts with backoff) keeps re-running the job until Resend accepts the email.
/// </summary>
public class HangfireContactEmailDispatcher : IContactEmailDispatcher
{
  private readonly IBackgroundJobClient _backgroundJobClient;

  public HangfireContactEmailDispatcher(IBackgroundJobClient backgroundJobClient)
  {
    _backgroundJobClient = backgroundJobClient;
  }

  public Task DispatchAsync(int submissionId)
  {
    _backgroundJobClient.Enqueue<IContactEmailJob>(job => job.SendAsync(submissionId));
    return Task.CompletedTask;
  }
}
