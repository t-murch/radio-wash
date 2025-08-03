using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Hubs;
using RadioWash.Api.Models;
using RadioWash.Api.Services.Implementations;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

public class ProgressBroadcastServiceTests
{
    private readonly Mock<IHubContext<PlaylistProgressHub, IPlaylistProgressClient>> _mockHubContext;
    private readonly Mock<ILogger<ProgressBroadcastService>> _mockLogger;
    private readonly Mock<IPlaylistProgressClient> _mockClient;
    private readonly Mock<IHubClients<IPlaylistProgressClient>> _mockClients;
    private readonly ProgressBroadcastService _service;

    public ProgressBroadcastServiceTests()
    {
        _mockHubContext = new Mock<IHubContext<PlaylistProgressHub, IPlaylistProgressClient>>();
        _mockLogger = new Mock<ILogger<ProgressBroadcastService>>();
        _mockClient = new Mock<IPlaylistProgressClient>();
        _mockClients = new Mock<IHubClients<IPlaylistProgressClient>>();

        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClient.Object);

        _service = new ProgressBroadcastService(_mockHubContext.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task BroadcastProgressUpdate_ShouldCallCorrectGroup()
    {
        // Arrange
        var jobId = 123;
        var update = new ProgressUpdate
        {
            Progress = 50,
            ProcessedTracks = 10,
            TotalTracks = 20,
            CurrentBatch = "Processing tracks 1-10",
            Message = "Processing: Track Name"
        };

        // Act
        await _service.BroadcastProgressUpdate(jobId, update);

        // Assert
        _mockClients.Verify(c => c.Group("job_123"), Times.Once);
        _mockClient.Verify(c => c.ProgressUpdate(update), Times.Once);
    }

    [Fact]
    public async Task BroadcastJobCompleted_ShouldCallCorrectGroupWithSuccessFlag()
    {
        // Arrange
        var jobId = 456;
        var message = "Job completed successfully";

        // Act
        await _service.BroadcastJobCompleted(jobId, message);

        // Assert
        _mockClients.Verify(c => c.Group("job_456"), Times.Once);
        _mockClient.Verify(c => c.JobCompleted(jobId, true, message), Times.Once);
    }

    [Fact]
    public async Task BroadcastJobCompleted_WithNullMessage_ShouldCallWithNullMessage()
    {
        // Arrange
        var jobId = 789;

        // Act
        await _service.BroadcastJobCompleted(jobId, null);

        // Assert
        _mockClient.Verify(c => c.JobCompleted(jobId, true, null), Times.Once);
    }

    [Fact]
    public async Task BroadcastJobFailed_ShouldCallCorrectGroupWithError()
    {
        // Arrange
        var jobId = 101;
        var error = "Something went wrong";

        // Act
        await _service.BroadcastJobFailed(jobId, error);

        // Assert
        _mockClients.Verify(c => c.Group("job_101"), Times.Once);
        _mockClient.Verify(c => c.JobFailed(jobId, error), Times.Once);
    }

    [Fact]
    public async Task BroadcastProgressUpdate_WhenExceptionThrown_ShouldLogError()
    {
        // Arrange
        var jobId = 111;
        var update = new ProgressUpdate { Progress = 25 };
        var expectedException = new InvalidOperationException("SignalR error");

        _mockClient.Setup(c => c.ProgressUpdate(It.IsAny<ProgressUpdate>()))
                  .ThrowsAsync(expectedException);

        // Act
        await _service.BroadcastProgressUpdate(jobId, update);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to broadcast progress update for job 111")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastJobCompleted_WhenExceptionThrown_ShouldLogError()
    {
        // Arrange
        var jobId = 222;
        var message = "Test completion";
        var expectedException = new InvalidOperationException("SignalR error");

        _mockClient.Setup(c => c.JobCompleted(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>()))
                  .ThrowsAsync(expectedException);

        // Act
        await _service.BroadcastJobCompleted(jobId, message);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to broadcast job completion for job 222")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastJobFailed_WhenExceptionThrown_ShouldLogError()
    {
        // Arrange
        var jobId = 333;
        var error = "Test error";
        var expectedException = new InvalidOperationException("SignalR error");

        _mockClient.Setup(c => c.JobFailed(It.IsAny<int>(), It.IsAny<string>()))
                  .ThrowsAsync(expectedException);

        // Act
        await _service.BroadcastJobFailed(jobId, error);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to broadcast job failure for job 333")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(12345)]
    public async Task BroadcastProgressUpdate_WithDifferentJobIds_ShouldUseCorrectGroupName(int jobId)
    {
        // Arrange
        var update = new ProgressUpdate { Progress = 75 };

        // Act
        await _service.BroadcastProgressUpdate(jobId, update);

        // Assert
        _mockClients.Verify(c => c.Group($"job_{jobId}"), Times.Once);
    }

    [Fact]
    public async Task BroadcastProgressUpdate_ShouldLogDebugMessage()
    {
        // Arrange
        var jobId = 555;
        var update = new ProgressUpdate
        {
            Progress = 80,
            Message = "Processing track"
        };

        // Act
        await _service.BroadcastProgressUpdate(jobId, update);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Broadcasted progress update for job 555: 80% - Processing track")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastJobCompleted_ShouldLogInformationMessage()
    {
        // Arrange
        var jobId = 666;
        var message = "All done!";

        // Act
        await _service.BroadcastJobCompleted(jobId, message);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Broadcasted job completion for job 666: All done!")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastJobFailed_ShouldLogWarningMessage()
    {
        // Arrange
        var jobId = 777;
        var error = "Failed to process";

        // Act
        await _service.BroadcastJobFailed(jobId, error);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Broadcasted job failure for job 777: Failed to process")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}