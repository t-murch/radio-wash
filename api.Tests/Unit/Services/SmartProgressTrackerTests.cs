using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for SmartProgressTracker
/// </summary>
public class SmartProgressTrackerTests
{
  [Fact]
  public void Initialize_SetsConfigurationCorrectly()
  {
    // Arrange
    var tracker = new SmartProgressTracker();
    var config = new BatchConfiguration(batchSize: 50, progressThreshold: 10, dbThreshold: 20);

    // Act
    tracker.Initialize(100, config);

    // Assert
    var update = tracker.CreateUpdate(50);
    Assert.Equal(50, update.Progress);
    Assert.Equal(50, update.ProcessedTracks);
    Assert.Equal(100, update.TotalTracks);
  }

  [Fact]
  public void ShouldReportProgress_ReturnsCorrectValues()
  {
    // Arrange
    var tracker = new SmartProgressTracker();
    var config = new BatchConfiguration(progressThreshold: 10);
    tracker.Initialize(100, config);

    // Act & Assert
    Assert.False(tracker.ShouldReportProgress(5)); // 5% - below threshold
    Assert.True(tracker.ShouldReportProgress(10));  // 10% - at threshold
    Assert.False(tracker.ShouldReportProgress(15)); // 15% - already reported at 10%
    Assert.True(tracker.ShouldReportProgress(20));  // 20% - next threshold
    Assert.True(tracker.ShouldReportProgress(100)); // 100% - always report completion
  }

  [Fact]
  public void ShouldPersistProgress_ReturnsCorrectValues()
  {
    // Arrange
    var tracker = new SmartProgressTracker();
    var config = new BatchConfiguration(dbThreshold: 20);
    tracker.Initialize(100, config);

    // Act & Assert
    Assert.False(tracker.ShouldPersistProgress(10)); // 10% - below threshold
    Assert.True(tracker.ShouldPersistProgress(20));  // 20% - at threshold
    Assert.False(tracker.ShouldPersistProgress(30)); // 30% - already persisted at 20%
    Assert.True(tracker.ShouldPersistProgress(40));  // 40% - next threshold
    Assert.True(tracker.ShouldPersistProgress(100)); // 100% - always persist completion
  }

  [Fact]
  public void CreateUpdate_ReturnsCorrectProgressUpdate()
  {
    // Arrange
    var tracker = new SmartProgressTracker();
    var config = new BatchConfiguration(batchSize: 25);
    tracker.Initialize(100, config);

    // Act
    var update = tracker.CreateUpdate(50, "Current Track");

    // Assert
    Assert.Equal(50, update.Progress);
    Assert.Equal(50, update.ProcessedTracks);
    Assert.Equal(100, update.TotalTracks);
    Assert.Equal("Batch 3", update.CurrentBatch); // (50 / 25) + 1 = 3
    Assert.Equal("Current Track", update.Message);
  }
}
