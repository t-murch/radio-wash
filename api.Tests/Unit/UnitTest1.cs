using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Tests.Unit;

/// <summary>
/// Tests for domain value objects
/// </summary>
public class DomainValueObjectTests
{
  [Fact]
  public void JobProgress_CalculatesPercentageCorrectly()
  {
    // Arrange & Act
    var progress = new JobProgress(25, 100, "Batch 1", 20);

    // Assert
    Assert.Equal(25, progress.ProcessedTracks);
    Assert.Equal(100, progress.TotalTracks);
    Assert.Equal(25, progress.PercentComplete);
    Assert.Equal("Batch 1", progress.CurrentBatch);
    Assert.Equal(20, progress.MatchedTracks);
  }

  [Fact]
  public void JobProgress_WithZeroTotal_ReturnsZeroPercent()
  {
    // Arrange & Act
    var progress = new JobProgress(0, 0, "Starting", 0);

    // Assert
    Assert.Equal(0, progress.PercentComplete);
  }

  [Fact]
  public void BatchConfiguration_UsesDefaultValues()
  {
    // Arrange & Act
    var config = new BatchConfiguration();

    // Assert
    Assert.Equal(100, config.BatchSize);
    Assert.Equal(5, config.ProgressReportingThreshold);
    Assert.Equal(10, config.DatabasePersistenceThreshold);
  }

  [Fact]
  public void BatchConfiguration_UsesCustomValues()
  {
    // Arrange & Act
    var config = new BatchConfiguration(50, 10, 20);

    // Assert
    Assert.Equal(50, config.BatchSize);
    Assert.Equal(10, config.ProgressReportingThreshold);
    Assert.Equal(20, config.DatabasePersistenceThreshold);
  }
}
