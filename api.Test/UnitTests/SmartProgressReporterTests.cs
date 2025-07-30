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
    [InlineData(20, 1)]    // Small playlist: 1 track per batch
    [InlineData(50, 2)]    // Small-medium: 2-3 tracks per batch 
    [InlineData(100, 5)]   // Medium playlist: 5 tracks per batch
    [InlineData(500, 25)]  // Large playlist: 25 tracks per batch
    [InlineData(1000, 50)] // Very large playlist: 50 tracks per batch
    public void Constructor_SetsCorrectBatchSize(int totalTracks, int expectedBatchSize)
    {
        // Act
        var reporter = new SmartProgressReporter(totalTracks);
        
        // Assert
        Assert.Equal(expectedBatchSize, reporter.BatchSize);
    }

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

    [Theory]
    [InlineData(20, 20)]   // Small playlist: expect ~20 updates
    [InlineData(50, 25)]   // Small-medium: expect ~25 updates
    [InlineData(100, 20)]  // Medium playlist: expect 20 updates
    [InlineData(500, 20)]  // Large playlist: expect 20 updates
    [InlineData(1000, 20)] // Very large playlist: expect 20 updates
    public void TotalExpectedUpdates_ReturnsCorrectValue(int totalTracks, int expectedUpdates)
    {
        // Arrange
        var reporter = new SmartProgressReporter(totalTracks);
        
        // Act
        var actualUpdates = reporter.TotalExpectedUpdates;
        
        // Assert
        Assert.Equal(expectedUpdates, actualUpdates);
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
        
        // Act & Assert - Should report at batch boundaries
        Assert.True(reporter.ShouldReportProgress(5));   // End of batch 1
        Assert.True(reporter.ShouldReportProgress(10));  // End of batch 2
        Assert.True(reporter.ShouldReportProgress(15));  // End of batch 3
        
        // Should not report in middle of batch
        Assert.False(reporter.ShouldReportProgress(3));  // Middle of batch 1
        Assert.False(reporter.ShouldReportProgress(7));  // Middle of batch 2
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
        Assert.Equal("Processing tracks 46-50", update.CurrentBatch);
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
        Assert.Equal("Processing tracks 21-25", update.CurrentBatch);
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

    [Theory]
    [InlineData(20, 5, 25)]    // 5th track of 20 = 25%
    [InlineData(50, 10, 20)]   // 10th track of 50 = 20%
    [InlineData(100, 25, 25)]  // 25th track of 100 = 25%
    [InlineData(500, 100, 20)] // 100th track of 500 = 20%
    [InlineData(1000, 200, 20)] // 200th track of 1000 = 20%
    public void CreateUpdate_CalculatesCorrectPercentage(int totalTracks, int currentTrack, int expectedPercentage)
    {
        // Arrange
        var reporter = new SmartProgressReporter(totalTracks);
        
        // Act
        var update = reporter.CreateUpdate(currentTrack);
        
        // Assert
        Assert.Equal(expectedPercentage, update.Progress);
    }

    [Fact]
    public void ProgressReporting_WorksCorrectlyForSmallPlaylist()
    {
        // Arrange - 20 track playlist
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
        
        // Assert - Should report for every track due to small size
        Assert.Equal(21, reportedTracks.Count); // 0-20 inclusive
        Assert.Contains(0, reportedTracks);   // Start
        Assert.Contains(20, reportedTracks);  // End
    }

    [Fact]
    public void ProgressReporting_WorksCorrectlyForLargePlaylist()
    {
        // Arrange - 1000 track playlist (50 tracks per batch)
        var reporter = new SmartProgressReporter(1000);
        var reportedTracks = new List<int>();
        
        // Act - Simulate processing all tracks
        for (int track = 0; track <= 1000; track++)
        {
            if (reporter.ShouldReportProgress(track))
            {
                reportedTracks.Add(track);
                var update = reporter.CreateUpdate(track);
                // Verify update is valid
                Assert.InRange(update.Progress, 0, 100);
                Assert.Equal(track, update.ProcessedTracks);
                Assert.Equal(1000, update.TotalTracks);
            }
        }
        
        // Assert - Should report approximately 20 times (5% increments)
        Assert.InRange(reportedTracks.Count, 18, 22); // Allow some variance
        Assert.Contains(0, reportedTracks);     // Start
        Assert.Contains(1000, reportedTracks); // End
        
        // Should report at 5% increments
        Assert.Contains(50, reportedTracks);   // 5%
        Assert.Contains(100, reportedTracks);  // 10%
        Assert.Contains(500, reportedTracks);  // 50%
    }

    [Fact]
    public void ProgressReporting_HandlesBatchBoundariesCorrectly()
    {
        // Arrange - 100 tracks (5 per batch)
        var reporter = new SmartProgressReporter(100);
        var batchMessages = new List<string>();
        
        // Act - Process tracks at specific batch boundaries
        var testTracks = new[] { 0, 5, 10, 15, 20, 50, 100 };
        foreach (var track in testTracks)
        {
            if (reporter.ShouldReportProgress(track))
            {
                var update = reporter.CreateUpdate(track);
                batchMessages.Add(update.CurrentBatch);
            }
        }
        
        // Assert - Verify batch messages are descriptive
        Assert.All(batchMessages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
        Assert.Contains(batchMessages, msg => msg.Contains("Starting batch") || msg.Contains("Processing tracks"));
    }

    [Fact]
    public void ShouldReportProgress_StatePersistsBetweenCalls()
    {
        // Arrange
        var reporter = new SmartProgressReporter(100);
        
        // Act & Assert - First call to track 5 should report
        Assert.True(reporter.ShouldReportProgress(5));
        
        // CreateUpdate to advance state
        reporter.CreateUpdate(5);
        
        // Second call to same track should not report
        Assert.False(reporter.ShouldReportProgress(5));
        
        // But next batch boundary should report
        Assert.True(reporter.ShouldReportProgress(10));
    }

    [Theory]
    [InlineData(1)]     // Edge case: single track
    [InlineData(2)]     // Edge case: two tracks
    [InlineData(19)]    // Edge case: just under 20
    [InlineData(21)]    // Edge case: just over 20
    [InlineData(999)]   // Edge case: just under 1000
    [InlineData(1001)]  // Edge case: just over 1000
    public void SmartProgressReporter_HandlesEdgeCases(int totalTracks)
    {
        // Arrange & Act
        var reporter = new SmartProgressReporter(totalTracks);
        
        // Assert - Should not throw and should have sensible values
        Assert.True(reporter.BatchSize > 0);
        Assert.True(reporter.TotalExpectedUpdates > 0);
        Assert.True(reporter.ShouldReportProgress(0));
        Assert.True(reporter.ShouldReportProgress(totalTracks));
        
        var startUpdate = reporter.CreateUpdate(0);
        Assert.Equal(0, startUpdate.Progress);
        Assert.Equal(totalTracks, startUpdate.TotalTracks);
        
        var endUpdate = reporter.CreateUpdate(totalTracks);
        Assert.Equal(100, endUpdate.Progress);
        Assert.Equal(totalTracks, endUpdate.TotalTracks);
    }
}