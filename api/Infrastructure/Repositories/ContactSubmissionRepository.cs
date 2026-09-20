using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class ContactSubmissionRepository : IContactSubmissionRepository
{
  private readonly RadioWashDbContext _dbContext;

  public ContactSubmissionRepository(RadioWashDbContext dbContext)
  {
    _dbContext = dbContext;
  }

  public async Task<ContactSubmission> AddAsync(ContactSubmission submission)
  {
    _dbContext.ContactSubmissions.Add(submission);
    await _dbContext.SaveChangesAsync();
    return submission;
  }

  public async Task<ContactSubmission?> GetByIdAsync(int id)
  {
    // Tracked on purpose: ContactEmailJob updates EmailStatus/EmailSentAt on this instance.
    return await _dbContext.ContactSubmissions
        .FirstOrDefaultAsync(cs => cs.Id == id);
  }

  public async Task SaveChangesAsync()
  {
    await _dbContext.SaveChangesAsync();
  }
}
