using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Tests.Unit.Infrastructure.Repositories;

/// <summary>
/// Unit tests for ContactSubmissionRepository using EF Core InMemory database
/// </summary>
public class ContactSubmissionRepositoryTests : RepositoryTestBase
{
  private readonly ContactSubmissionRepository _repository;

  public ContactSubmissionRepositoryTests()
  {
    _repository = new ContactSubmissionRepository(_context);
  }

  [Fact]
  public async Task AddAsync_PersistsAllFields()
  {
    // Arrange
    var submission = new ContactSubmission
    {
      Name = "Ada Lovelace",
      Email = "ada@example.com",
      Message = "The sync feature stopped working for me yesterday.",
      SupabaseUserId = "sb_user_1",
      ClientIp = "203.0.113.7",
      EmailStatus = ContactEmailStatus.Pending,
      CreatedAt = DateTime.UtcNow
    };

    // Act
    var added = await _repository.AddAsync(submission);

    // Assert
    Assert.True(added.Id > 0);
    DetachAllEntities();
    var persisted = await _repository.GetByIdAsync(added.Id);
    Assert.NotNull(persisted);
    Assert.Equal("Ada Lovelace", persisted!.Name);
    Assert.Equal("ada@example.com", persisted.Email);
    Assert.Equal("The sync feature stopped working for me yesterday.", persisted.Message);
    Assert.Equal("sb_user_1", persisted.SupabaseUserId);
    Assert.Equal("203.0.113.7", persisted.ClientIp);
    Assert.Equal(ContactEmailStatus.Pending, persisted.EmailStatus);
    Assert.Null(persisted.EmailSentAt);
    Assert.Null(persisted.EmailError);
  }

  [Fact]
  public async Task GetByIdAsync_WithUnknownId_ReturnsNull()
  {
    // Act
    var result = await _repository.GetByIdAsync(9999);

    // Assert
    Assert.Null(result);
  }
}
