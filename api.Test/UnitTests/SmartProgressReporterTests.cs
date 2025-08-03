using RadioWash.Api.Services;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

/// <summary>
/// Comprehensive unit tests for SmartProgressReporter class to verify
/// percentage-based batching logic and progress reporting behavior.
/// </summary>
public class SmartProgressReporterTests
{

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(-100)]
  public void Constructor_ThrowsForInvalidTotalTracks(int invalidTotalTracks)
  {
    // Act & Assert
    var exception = Assert.Throws<ArgumentException>(() => new SmartProgressReporter(invalidTotalTracks));
    Assert.Contains("Total tracks must be greater than zero", exception.Message);
  }


  [Fact]
  public void ShouldReportProgress_ReportsForFirstTrack()
  {
    // Arrange
    var reporter = new SmartProgressReporter(100);

    // Act
    var shouldReport = reporter.ShouldReportProgress(0);

    // Assert
    Assert.True(shouldReport);
  }

  [Fact]
  public void ShouldReportProgress_ReportsForLastTrack()
  {
    // Arrange
    var reporter = new SmartProgressReporter(100);

    // Act
    var shouldReport = reporter.ShouldReportProgress(100);

    // Assert
    Assert.True(shouldReport);
  }

  [Fact]
  public void ShouldReportProgress_ReportsAtBatchBoundaries()
  {
    // Arrange - 100 tracks = 5 tracks per batch
    var reporter = new SmartProgressReporter(100);

    // Act & Assert - Start with track 0 (always reports)
    Assert.True(reporter.ShouldReportProgress(0));
    reporter.CreateUpdate(0); // Update state
    
    // Should NOT report within same batch (batch 0: tracks 0-4)
    Assert.False(reporter.ShouldReportProgress(3));  // Middle of batch 0
    Assert.False(reporter.ShouldReportProgress(4));  // End of batch 0
    
    // Should report when entering new batch (batch 1: tracks 5-9)
    Assert.True(reporter.ShouldReportProgress(5));   // Start of batch 1
    reporter.CreateUpdate(5); // Update state
    
    // Should not report within same batch
    Assert.False(reporter.ShouldReportProgress(7));  // Middle of batch 1
    Assert.False(reporter.ShouldReportProgress(9));  // End of batch 1
    
    // Should report when entering next batch (batch 2: tracks 10-14)
    Assert.True(reporter.ShouldReportProgress(10));  // Start of batch 2
  }

  [Fact]
  public void ShouldReportProgress_ThrowsForOutOfRangeValues()
  {
    // Arrange
    var reporter = new SmartProgressReporter(100);

    // Act & Assert
    Assert.Throws<ArgumentOutOfRangeException>(() => reporter.ShouldReportProgress(-1));
    Assert.Throws<ArgumentOutOfRangeException>(() => reporter.ShouldReportProgress(101));
  }

  [Fact]
  public void CreateUpdate_InitialTrack_ReturnsCorrectValues()
  {
    // Arrange
    var reporter = new SmartProgressReporter(100);

    // Act
    var update = reporter.CreateUpdate(0);

    // Assert
    Assert.Equal(0, update.Progress);
    Assert.Equal(0, update.ProcessedTracks);
    Assert.Equal(100, update.TotalTracks);
    Assert.Equal("Starting batch 1", update.CurrentBatch);
    Assert.Equal("Initializing playlist processing...", update.Message);
  }

  [Fact]
  public void CreateUpdate_FinalTrack_ReturnsCorrectValues()
  {
    // Arrange
    var reporter = new SmartProgressReporter(100);

    // Act
    var update = reporter.CreateUpdate(100);

    // Assert
    Assert.Equal(100, update.Progress);
    Assert.Equal(100, update.ProcessedTracks);
    Assert.Equal(100, update.TotalTracks);
    Assert.Contains("Completed all", update.CurrentBatch);
    Assert.Equal("Finalizing playlist creation...", update.Message);
  }

  [Fact]
  public void CreateUpdate_MiddleTrack_ReturnsCorrectValues()
  {
    // Arrange - 100 tracks = 5 tracks per batch
    var reporter = new SmartProgressReporter(100);

    // Act
    var update = reporter.CreateUpdate(50, "Test Track");

    // Assert
    Assert.Equal(50, update.Progress);
    Assert.Equal(50, update.ProcessedTracks);
    Assert.Equal(100, update.TotalTracks);
    Assert.Equal("Processing tracks 51-55", update.CurrentBatch); // Track 50 is in batch 10 (tracks 50-54, display 51-55)
    Assert.Equal("Processing: Test Track", update.Message);
  }

  [Fact]
  public void CreateUpdate_MiddleTrackNoName_ReturnsCorrectValues()
  {
    // Arrange - 100 tracks = 5 tracks per batch
    var reporter = new SmartProgressReporter(100);

    // Act
    var update = reporter.CreateUpdate(25);

    // Assert
    Assert.Equal(25, update.Progress);
    Assert.Equal(25, update.ProcessedTracks);
    Assert.Equal(100, update.TotalTracks);
    Assert.Equal("Processing tracks 26-30", update.CurrentBatch);
    Assert.Contains("Processing batch", update.Message);
  }

  [Fact]
  public void CreateUpdate_ThrowsForOutOfRangeValues()
  {
    // Arrange
    var reporter = new SmartProgressReporter(100);

    // Act & Assert
    Assert.Throws<ArgumentOutOfRangeException>(() => reporter.CreateUpdate(-1));
    Assert.Throws<ArgumentOutOfRangeException>(() => reporter.CreateUpdate(101));
  }


  [Fact]
  public void ProgressReporting_WorksCorrectlyForSmallPlaylist()
  {
    // Arrange - 20 track playlist (batch size = 1, so reports every track)
    var reporter = new SmartProgressReporter(20);
    var reportedTracks = new List<int>();

    // Act - Simulate processing all tracks
    for (int track = 0; track <= 20; track++)
    {
      if (reporter.ShouldReportProgress(track))
      {
        reportedTracks.Add(track);
        var update = reporter.CreateUpdate(track);
        // Verify update is valid
        Assert.InRange(update.Progress, 0, 100);
        Assert.Equal(track, update.ProcessedTracks);
        Assert.Equal(20, update.TotalTracks);
      }
    }

    // Assert - Should report for every track due to batch size of 1
    Assert.Equal(21, reportedTracks.Count); // 0-20 inclusive, each track in its own batch
    Assert.Contains(0, reportedTracks);   // Start
    Assert.Contains(20, reportedTracks);  // End
    Assert.Contains(1, reportedTracks);   // Track 1 should now be in its own batch
  }
}