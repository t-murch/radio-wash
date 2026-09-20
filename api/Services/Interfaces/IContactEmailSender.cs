using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

public interface IContactEmailSender
{
  /// <summary>
  /// Sends the owner-notification email for a submission. Throws on delivery failure so the
  /// caller (the email job) can record the error and let Hangfire retry.
  /// </summary>
  Task SendAsync(ContactSubmission submission, CancellationToken cancellationToken = default);
}
