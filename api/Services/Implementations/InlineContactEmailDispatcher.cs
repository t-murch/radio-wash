using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Dispatcher for environments without Hangfire (tests): runs the email job inline so the
/// full persist → send path is exercised in-request. Send failures are logged, not thrown —
/// the job has already recorded them on the submission row.
/// </summary>
public class InlineContactEmailDispatcher : IContactEmailDispatcher
{
  private readonly IContactEmailJob _job;
  private readonly ILogger<InlineContactEmailDispatcher> _logger;

  public InlineContactEmailDispatcher(IContactEmailJob job, ILogger<InlineContactEmailDispatcher> logger)
  {
    _job = job;
    _logger = logger;
  }

  public async Task DispatchAsync(int submissionId)
  {
    try
    {
      await _job.SendAsync(submissionId);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Inline contact email send failed for submission {SubmissionId}", submissionId);
    }
  }
}
