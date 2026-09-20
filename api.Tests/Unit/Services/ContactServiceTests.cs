using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for ContactService
/// Tests the persist-first contract: the submission must be durable before the email is
/// dispatched, and a dispatch failure must never surface to the caller
/// </summary>
public class ContactServiceTests
{
  private readonly Mock<IContactSubmissionRepository> _mockRepository;
  private readonly Mock<IContactEmailDispatcher> _mockDispatcher;
  private readonly Mock<ILogger<ContactService>> _mockLogger;
  private readonly ContactService _service;

  public ContactServiceTests()
  {
    _mockRepository = new Mock<IContactSubmissionRepository>();
    _mockDispatcher = new Mock<IContactEmailDispatcher>();
    _mockLogger = new Mock<ILogger<ContactService>>();

    // AddAsync assigns the database id, which the dispatcher call depends on.
    _mockRepository
        .Setup(x => x.AddAsync(It.IsAny<ContactSubmission>()))
        .ReturnsAsync((ContactSubmission s) =>
        {
          s.Id = 42;
          return s;
        });

    _service = new ContactService(_mockRepository.Object, _mockDispatcher.Object, _mockLogger.Object);
  }

  [Fact]
  public async Task SubmitAsync_PersistsSubmissionBeforeDispatching()
  {
    // Arrange
    var callOrder = new List<string>();
    _mockRepository
        .Setup(x => x.AddAsync(It.IsAny<ContactSubmission>()))
        .Callback(() => callOrder.Add("persist"))
        .ReturnsAsync((ContactSubmission s) =>
        {
          s.Id = 42;
          return s;
        });
    _mockDispatcher
        .Setup(x => x.DispatchAsync(It.IsAny<int>()))
        .Callback(() => callOrder.Add("dispatch"))
        .Returns(Task.CompletedTask);

    // Act
    await _service.SubmitAsync(CreateValidDto(), "203.0.113.7", null);

    // Assert: the row is durable before any delivery attempt, and the dispatch uses the
    // id the save produced.
    Assert.Equal(new[] { "persist", "dispatch" }, callOrder);
    _mockDispatcher.Verify(x => x.DispatchAsync(42), Times.Once);
  }

  [Fact]
  public async Task SubmitAsync_WhenDispatcherThrows_DoesNotThrowAndSubmissionRemainsPersisted()
  {
    // Arrange
    _mockDispatcher
        .Setup(x => x.DispatchAsync(It.IsAny<int>()))
        .ThrowsAsync(new InvalidOperationException("Hangfire storage unavailable"));

    // Act: must not throw — the message is already stored and retryable.
    await _service.SubmitAsync(CreateValidDto(), null, null);

    // Assert
    _mockRepository.Verify(x => x.AddAsync(It.IsAny<ContactSubmission>()), Times.Once);
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to dispatch contact email")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
  }

  [Fact]
  public async Task SubmitAsync_SetsPendingStatusAndUtcCreatedAt()
  {
    // Arrange
    ContactSubmission? captured = null;
    _mockRepository
        .Setup(x => x.AddAsync(It.IsAny<ContactSubmission>()))
        .Callback((ContactSubmission s) => captured = s)
        .ReturnsAsync((ContactSubmission s) => s);

    var before = DateTime.UtcNow;

    // Act
    await _service.SubmitAsync(CreateValidDto(), "203.0.113.7", "sb_user_1");

    // Assert
    Assert.NotNull(captured);
    Assert.Equal(ContactEmailStatus.Pending, captured!.EmailStatus);
    Assert.Equal(DateTimeKind.Utc, captured.CreatedAt.Kind);
    Assert.InRange(captured.CreatedAt, before, DateTime.UtcNow);
    Assert.Equal("203.0.113.7", captured.ClientIp);
    Assert.Equal("sb_user_1", captured.SupabaseUserId);
  }

  private static ContactSubmissionDto CreateValidDto()
  {
    return new ContactSubmissionDto
    {
      Name = "Ada Lovelace",
      Email = "ada@example.com",
      Message = "The sync feature stopped working for me yesterday."
    };
  }
}
