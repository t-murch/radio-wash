using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Tests for the startup-time recurring-job registration. Registration is idempotent and
/// the definitions persist in Hangfire storage, so the contract under test is resilience:
/// a failing AddOrUpdate (e.g. a distributed-lock timeout left by an unclean shutdown)
/// must not escape and take the API down, and must not stop the other jobs from
/// registering.
/// </summary>
public class SyncSchedulerServiceTests
{
  private readonly Mock<IRecurringJobManager> _recurringJobManager = new();
  private readonly Mock<IBackgroundJobClient> _backgroundJobClient = new();
  private readonly Mock<IUnitOfWork> _unitOfWork = new();
  private readonly Mock<ISubscriptionService> _subscriptionService = new();
  private readonly Mock<ILogger<SyncSchedulerService>> _logger = new();
  private readonly SyncSchedulerService _service;

  public SyncSchedulerServiceTests()
  {
    _service = new SyncSchedulerService(
      _recurringJobManager.Object,
      _backgroundJobClient.Object,
      _unitOfWork.Object,
      _subscriptionService.Object,
      _logger.Object);
  }

  [Fact]
  public void InitializeScheduledJobs_RegistersBothRecurringJobs()
  {
    _service.InitializeScheduledJobs();

    _recurringJobManager.Verify(m => m.AddOrUpdate(
      "playlist-sync-processor", It.IsAny<Job>(), "1 0 * * *", It.IsAny<RecurringJobOptions>()), Times.Once);
    _recurringJobManager.Verify(m => m.AddOrUpdate(
      "subscription-validator", It.IsAny<Job>(), "0 2 * * *", It.IsAny<RecurringJobOptions>()), Times.Once);
  }

  [Fact]
  public void InitializeScheduledJobs_SurvivesARegistrationFailureAndRegistersTheRemainingJobs()
  {
    // The real-world shape: an API instance killed while holding Hangfire's distributed
    // lock leaves the lock row orphaned; until the 10-minute staleness cutoff passes,
    // AddOrUpdate for that job times out. This used to escape as an unhandled exception
    // in Program and crash-loop the API on every quick restart.
    _recurringJobManager
      .Setup(m => m.AddOrUpdate(
        "playlist-sync-processor", It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()))
      .Throws(new TimeoutException(
        "Timeout expired. The timeout elapsed prior to obtaining a distributed lock on the " +
        "'hangfire:lock:recurring-job:playlist-sync-processor' resource."));

    var exception = Record.Exception(() => _service.InitializeScheduledJobs());

    Assert.Null(exception);
    // The failure is isolated: the other job still registers.
    _recurringJobManager.Verify(m => m.AddOrUpdate(
      "subscription-validator", It.IsAny<Job>(), "0 2 * * *", It.IsAny<RecurringJobOptions>()), Times.Once);
    // And it is loud, not swallowed.
    _logger.Verify(
      x => x.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("playlist-sync-processor")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Once);
  }
}
