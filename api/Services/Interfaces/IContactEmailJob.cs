namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// The retryable unit of contact-email delivery. Interface-typed so Hangfire can enqueue it
/// (<c>Enqueue&lt;IContactEmailJob&gt;</c>) and resolve the implementation per execution.
/// </summary>
public interface IContactEmailJob
{
  Task SendAsync(int submissionId);
}
