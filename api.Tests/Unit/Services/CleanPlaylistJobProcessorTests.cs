using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Characterization tests for <see cref="CleanPlaylistJobProcessor"/>. These tests pin the
/// observable behavior of <see cref="CleanPlaylistJobProcessor.ProcessJobAsync"/> as it exists
/// today so the multi-platform refactor can swap its collaborators without regression.
/// </summary>
public class CleanPlaylistJobProcessorTests
{
  private readonly Mock<IUnitOfWork> _mockUnitOfWork;
  private readonly Mock<IPlaylistCleanerFactory> _mockCleanerFactory;
  private readonly Mock<IPlaylistCleaner> _mockCleaner;
  private readonly Mock<IProgressBroadcastService> _mockProgressService;
  private readonly Mock<ILogger<CleanPlaylistJobProcessor>> _mockLogger;
  private readonly Mock<ICleanPlaylistJobRepository> _mockJobRepo;
  private readonly Mock<IUserRepository> _mockUserRepo;
  private readonly CleanPlaylistJobProcessor _processor;

  public CleanPlaylistJobProcessorTests()
  {
    _mockUnitOfWork = new Mock<IUnitOfWork>();
    _mockCleanerFactory = new Mock<IPlaylistCleanerFactory>();
    _mockCleaner = new Mock<IPlaylistCleaner>();
    _mockProgressService = new Mock<IProgressBroadcastService>();
    _mockLogger = new Mock<ILogger<CleanPlaylistJobProcessor>>();
    _mockJobRepo = new Mock<ICleanPlaylistJobRepository>();
    _mockUserRepo = new Mock<IUserRepository>();

    _mockUnitOfWork.Setup(x => x.Jobs).Returns(_mockJobRepo.Object);
    _mockUnitOfWork.Setup(x => x.Users).Returns(_mockUserRepo.Object);

    _mockCleanerFactory
      .Setup(x => x.CreateCleaner(It.IsAny<string>()))
      .Returns(_mockCleaner.Object);

    _processor = new CleanPlaylistJobProcessor(
      _mockUnitOfWork.Object,
      _mockCleanerFactory.Object,
      _mockProgressService.Object,
      _mockLogger.Object);
  }

  [Fact]
  public async Task ProcessJobAsync_HappyPath_TransitionsJobThroughProcessingToCompleted()
  {
    // Arrange
    var jobId = 42;
    var userId = 7;
    var job = new CleanPlaylistJob
    {
      Id = jobId,
      UserId = userId,
      SourcePlaylistId = "src",
      SourcePlaylistName = "Source",
      TargetPlaylistName = "Clean - Source",
      Status = JobStatus.Pending,
      TotalTracks = 10
    };
    var user = new User { Id = userId, SupabaseId = "sb-abc" };
    var cleaningResult = new PlaylistCleaningResult
    {
      ProcessedTracks = 10,
      MatchedTracks = 8,
      TargetPlaylistId = "target-playlist-id",
      CleanTrackUris = new List<string> { "spotify:track:1", "spotify:track:2" }
    };

    _mockJobRepo.Setup(x => x.GetByIdAsync(jobId)).ReturnsAsync(job);
    _mockUserRepo.Setup(x => x.GetByIdAsync(userId)).ReturnsAsync(user);
    _mockCleaner
      .Setup(x => x.CleanPlaylistAsync(job, user))
      .ReturnsAsync(cleaningResult);

    var observedStatuses = new List<string>();
    _mockJobRepo
      .Setup(x => x.UpdateAsync(It.IsAny<CleanPlaylistJob>()))
      .Callback<CleanPlaylistJob>(j => observedStatuses.Add(j.Status))
      .ReturnsAsync((CleanPlaylistJob j) => j);

    // Act
    await _processor.ProcessJobAsync(jobId);

    // Assert — status transitioned Pending → Processing → Completed
    Assert.Contains(JobStatus.Processing, observedStatuses);
    Assert.Equal(JobStatus.Completed, job.Status);

    // Assert — result fields persisted on the job entity
    Assert.Equal(10, job.ProcessedTracks);
    Assert.Equal(8, job.MatchedTracks);
    Assert.Equal("target-playlist-id", job.TargetPlaylistId);
    Assert.Equal("Completed", job.CurrentBatch);

    // Assert — cleaner was resolved via factory (currently always with "spotify")
    _mockCleanerFactory.Verify(x => x.CreateCleaner("spotify"), Times.Once);

    // Assert — completion broadcast fires exactly once
    _mockProgressService.Verify(
      x => x.BroadcastJobCompleted(jobId, It.IsAny<string>()),
      Times.Once);
    _mockProgressService.Verify(
      x => x.BroadcastJobFailed(It.IsAny<int>(), It.IsAny<string>()),
      Times.Never);

    // Assert — SaveChangesAsync invoked for each UpdateAsync (processing + completed)
    _mockUnitOfWork.Verify(x => x.SaveChangesAsync(), Times.AtLeast(2));
  }

  [Fact]
  public async Task ProcessJobAsync_WhenJobNotFound_LogsErrorAndReturnsWithoutThrowing()
  {
    // Arrange
    var jobId = 999;
    _mockJobRepo.Setup(x => x.GetByIdAsync(jobId)).ReturnsAsync((CleanPlaylistJob?)null);

    // Act — must not throw
    await _processor.ProcessJobAsync(jobId);

    // Assert — no status update, no cleaner invocation, no broadcast
    _mockJobRepo.Verify(x => x.UpdateAsync(It.IsAny<CleanPlaylistJob>()), Times.Never);
    _mockCleanerFactory.Verify(x => x.CreateCleaner(It.IsAny<string>()), Times.Never);
    _mockCleaner.Verify(
      x => x.CleanPlaylistAsync(It.IsAny<CleanPlaylistJob>(), It.IsAny<User>()),
      Times.Never);
    _mockProgressService.Verify(
      x => x.BroadcastJobCompleted(It.IsAny<int>(), It.IsAny<string>()),
      Times.Never);
    _mockProgressService.Verify(
      x => x.BroadcastJobFailed(It.IsAny<int>(), It.IsAny<string>()),
      Times.Never);

    // Assert — error logged with the missing job ID
    _mockLogger.Verify(
      l => l.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(jobId.ToString())),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.AtLeastOnce);
  }

  [Fact]
  public async Task ProcessJobAsync_WhenCleanerThrows_MarksJobFailedAndDoesNotRethrow()
  {
    // Arrange
    var jobId = 42;
    var userId = 7;
    var job = new CleanPlaylistJob
    {
      Id = jobId,
      UserId = userId,
      SourcePlaylistId = "src",
      SourcePlaylistName = "Source",
      TargetPlaylistName = "Clean - Source",
      Status = JobStatus.Pending,
      TotalTracks = 10
    };
    var user = new User { Id = userId, SupabaseId = "sb-abc" };

    _mockJobRepo.Setup(x => x.GetByIdAsync(jobId)).ReturnsAsync(job);
    _mockUserRepo.Setup(x => x.GetByIdAsync(userId)).ReturnsAsync(user);
    _mockCleaner
      .Setup(x => x.CleanPlaylistAsync(job, user))
      .ThrowsAsync(new InvalidOperationException("Spotify exploded"));

    // Act — must not rethrow; Hangfire retry policy owns retries via [AutomaticRetry]
    await _processor.ProcessJobAsync(jobId);

    // Assert — error persisted via repository
    _mockJobRepo.Verify(
      x => x.UpdateErrorAsync(jobId, "Spotify exploded"),
      Times.Once);

    // Assert — failure broadcast fires, success broadcast does not
    _mockProgressService.Verify(
      x => x.BroadcastJobFailed(jobId, "Spotify exploded"),
      Times.Once);
    _mockProgressService.Verify(
      x => x.BroadcastJobCompleted(It.IsAny<int>(), It.IsAny<string>()),
      Times.Never);
  }

  [Fact]
  public async Task ProcessJobAsync_WhenFailureHandlingItselfThrows_SwallowsInnerExceptionAndLogs()
  {
    // Arrange
    var jobId = 42;
    var userId = 7;
    var job = new CleanPlaylistJob
    {
      Id = jobId,
      UserId = userId,
      SourcePlaylistId = "src",
      SourcePlaylistName = "Source",
      TargetPlaylistName = "Clean - Source",
      Status = JobStatus.Pending
    };
    var user = new User { Id = userId, SupabaseId = "sb-abc" };

    _mockJobRepo.Setup(x => x.GetByIdAsync(jobId)).ReturnsAsync(job);
    _mockUserRepo.Setup(x => x.GetByIdAsync(userId)).ReturnsAsync(user);
    _mockCleaner
      .Setup(x => x.CleanPlaylistAsync(job, user))
      .ThrowsAsync(new InvalidOperationException("Primary failure"));

    // The error-persist path throws — simulates a transient DB failure during failure handling
    _mockJobRepo
      .Setup(x => x.UpdateErrorAsync(jobId, It.IsAny<string>()))
      .ThrowsAsync(new Exception("DB unreachable"));

    // Act — must not surface either exception to Hangfire
    await _processor.ProcessJobAsync(jobId);

    // Assert — outer error was logged (primary failure), and inner error was logged separately
    _mockLogger.Verify(
      l => l.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception?>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.AtLeast(2));
  }
}
