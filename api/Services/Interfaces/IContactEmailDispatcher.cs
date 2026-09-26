namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Seam between "the submission is saved" and "the email gets sent". Production dispatches to
/// Hangfire for automatic retries; test environments (where Hangfire isn't registered) run the
/// email job inline.
/// </summary>
public interface IContactEmailDispatcher
{
  Task DispatchAsync(int submissionId);
}
