using RadioWash.Api.Models.DTO;

namespace RadioWash.Api.Services.Interfaces;

public interface IContactService
{
  /// <summary>
  /// Persists a contact-form submission and dispatches the notification email. The submission
  /// is durable once this returns; a dispatch failure is logged, not thrown.
  /// </summary>
  Task SubmitAsync(ContactSubmissionDto dto, string? clientIp, string? supabaseUserId);
}
