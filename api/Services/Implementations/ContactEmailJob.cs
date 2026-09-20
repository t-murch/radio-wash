using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class ContactEmailJob : IContactEmailJob
{
  private const int MaxErrorLength = 2000;

  private readonly IContactSubmissionRepository _repository;
  private readonly IContactEmailSender _emailSender;
  private readonly ILogger<ContactEmailJob> _logger;

  public ContactEmailJob(
      IContactSubmissionRepository repository,
      IContactEmailSender emailSender,
      ILogger<ContactEmailJob> logger)
  {
    _repository = repository;
    _emailSender = emailSender;
    _logger = logger;
  }

  public async Task SendAsync(int submissionId)
  {
    var submission = await _repository.GetByIdAsync(submissionId);
    if (submission == null)
    {
      _logger.LogWarning("Contact submission {SubmissionId} not found; skipping email send", submissionId);
      return;
    }

    // A Hangfire retry after a crash between send and save must not email the owner twice.
    if (submission.EmailStatus == ContactEmailStatus.Sent)
    {
      return;
    }

    try
    {
      await _emailSender.SendAsync(submission);
    }
    catch (Exception ex)
    {
      submission.EmailStatus = ContactEmailStatus.Failed;
      submission.EmailError = ex.Message.Length <= MaxErrorLength ? ex.Message : ex.Message[..MaxErrorLength];
      await _repository.SaveChangesAsync();
      // Rethrow so Hangfire's automatic retry gets another attempt at delivery.
      throw;
    }

    submission.EmailStatus = ContactEmailStatus.Sent;
    submission.EmailSentAt = DateTime.UtcNow;
    submission.EmailError = null;
    await _repository.SaveChangesAsync();
  }
}
