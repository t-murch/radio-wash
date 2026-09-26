using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for ContactEmailJob
/// Tests delivery bookkeeping: Sent is terminal (idempotent under Hangfire retries), and a
/// sender failure records the error, marks the row Failed, and rethrows so Hangfire retries
/// </summary>
public class ContactEmailJobTests
{
  private readonly Mock<IContactSubmissionRepository> _mockRepository;
  private readonly Mock<IContactEmailSender> _mockEmailSender;
  private readonly Mock<ILogger<ContactEmailJob>> _mockLogger;
  private readonly ContactEmailJob _job;

  public ContactEmailJobTests()
  {
    _mockRepository = new Mock<IContactSubmissionRepository>();
    _mockEmailSender = new Mock<IContactEmailSender>();
    _mockLogger = new Mock<ILogger<ContactEmailJob>>();

    _job = new ContactEmailJob(_mockRepository.Object, _mockEmailSender.Object, _mockLogger.Object);
  }

  [Fact]
  public async Task SendAsync_WithPendingSubmission_SendsEmailAndMarksSent()
  {
    // Arrange
    var submission = CreateTestSubmission();
    _mockRepository.Setup(x => x.GetByIdAsync(submission.Id)).ReturnsAsync(submission);

    // Act
    await _job.SendAsync(submission.Id);

    // Assert
    _mockEmailSender.Verify(x => x.SendAsync(submission, It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal(ContactEmailStatus.Sent, submission.EmailStatus);
    Assert.NotNull(submission.EmailSentAt);
    Assert.Null(submission.EmailError);
    _mockRepository.Verify(x => x.SaveChangesAsync(), Times.Once);
  }

  [Fact]
  public async Task SendAsync_WhenAlreadySent_DoesNotSendAgain()
  {
    // Arrange: a Hangfire retry after a crash between send and save must not double-email.
    var submission = CreateTestSubmission();
    submission.EmailStatus = ContactEmailStatus.Sent;
    submission.EmailSentAt = DateTime.UtcNow.AddMinutes(-5);
    _mockRepository.Setup(x => x.GetByIdAsync(submission.Id)).ReturnsAsync(submission);

    // Act
    await _job.SendAsync(submission.Id);

    // Assert
    _mockEmailSender.Verify(
        x => x.SendAsync(It.IsAny<ContactSubmission>(), It.IsAny<CancellationToken>()),
        Times.Never);
    _mockRepository.Verify(x => x.SaveChangesAsync(), Times.Never);
  }

  [Fact]
  public async Task SendAsync_WhenSenderThrows_MarksFailedRecordsErrorAndRethrows()
  {
    // Arrange
    var submission = CreateTestSubmission();
    _mockRepository.Setup(x => x.GetByIdAsync(submission.Id)).ReturnsAsync(submission);
    _mockEmailSender
        .Setup(x => x.SendAsync(submission, It.IsAny<CancellationToken>()))
        .ThrowsAsync(new HttpRequestException("Resend returned 500"));

    // Act: the rethrow is what makes Hangfire schedule the next attempt.
    await Assert.ThrowsAsync<HttpRequestException>(() => _job.SendAsync(submission.Id));

    // Assert
    Assert.Equal(ContactEmailStatus.Failed, submission.EmailStatus);
    Assert.Contains("Resend returned 500", submission.EmailError);
    Assert.Null(submission.EmailSentAt);
    _mockRepository.Verify(x => x.SaveChangesAsync(), Times.Once);
  }

  private static ContactSubmission CreateTestSubmission()
  {
    return new ContactSubmission
    {
      Id = 42,
      Name = "Ada Lovelace",
      Email = "ada@example.com",
      Message = "The sync feature stopped working for me yesterday.",
      EmailStatus = ContactEmailStatus.Pending,
      CreatedAt = DateTime.UtcNow.AddMinutes(-1)
    };
  }
}
