using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class ContactService : IContactService
{
  private readonly IContactSubmissionRepository _repository;
  private readonly IContactEmailDispatcher _dispatcher;
  private readonly ILogger<ContactService> _logger;

  public ContactService(
      IContactSubmissionRepository repository,
      IContactEmailDispatcher dispatcher,
      ILogger<ContactService> logger)
  {
    _repository = repository;
    _dispatcher = dispatcher;
    _logger = logger;
  }

  public async Task SubmitAsync(ContactSubmissionDto dto, string? clientIp, string? supabaseUserId)
  {
    var submission = new ContactSubmission
    {
      Name = dto.Name?.Trim() ?? string.Empty,
      Email = dto.Email?.Trim() ?? string.Empty,
      Message = dto.Message?.Trim() ?? string.Empty,
      SupabaseUserId = supabaseUserId,
      ClientIp = clientIp,
      EmailStatus = ContactEmailStatus.Pending,
      CreatedAt = DateTime.UtcNow
    };

    await _repository.AddAsync(submission);

    // The submission is durable at this point. A dispatch failure (Hangfire storage down,
    // inline send blowing up) must not turn a stored message into a user-facing error — the
    // row stays Pending/Failed and delivery can be retried.
    try
    {
      await _dispatcher.DispatchAsync(submission.Id);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to dispatch contact email for submission {SubmissionId}", submission.Id);
    }
  }
}
