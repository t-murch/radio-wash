using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface IContactSubmissionRepository
{
  Task<ContactSubmission> AddAsync(ContactSubmission submission);

  /// <summary>
  /// Loads a submission as a tracked entity so callers can mutate delivery state and persist
  /// it with <see cref="SaveChangesAsync"/>.
  /// </summary>
  Task<ContactSubmission?> GetByIdAsync(int id);

  Task SaveChangesAsync();
}
