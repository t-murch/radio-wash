using RadioWash.Api.Services;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

/// <summary>
/// Scaling tests for SmartProgressReporter to verify it handles various
/// playlist sizes efficiently and provides consistent user experience.
/// </summary>
public class SmartProgressReporterScalingTests
{
    [Theory]
    [InlineData(20)]    // Small playlist
    [InlineData(50)]    // Small-medium playlist
    [InlineData(100)]   // Medium playlist
    [InlineData(500)]   // Large playlist
    [InlineData(1000)]  // Very large playlist
    [InlineData(2000)]  // Extra large playlist
    public void SmartProgressReporter_SimulateFullPlaylistProcessing(int totalTracks)
    {
        // Arrange
        var reporter = new SmartProgressReporter(totalTracks);
        var progressUpdates = new List<(int track, int progress, string batch)>();
        
        // Act - Simulate processing all tracks
        for (int track = 0; track <= totalTracks; track++)
        {
            if (reporter.ShouldReportProgress(track))
            {
                var update = reporter.CreateUpdate(track, $"Track {track}");
                progressUpdates.Add((track, update.Progress, update.CurrentBatch));
            }
        }
        
        // Assert - Verify scaling characteristics
        VerifyScalingCharacteristics(totalTracks, progressUpdates, reporter);
    }

    private void VerifyScalingCharacteristics(
        int totalTracks,
        List<(int track, int progress, string batch)> progressUpdates,
        SmartProgressReporter reporter)
    {
        // 1. Verify update frequency is reasonable (not too many, not too few)
        if (totalTracks <= 50)
        {
            // Small playlists: Allow more frequent updates
            Assert.InRange(progressUpdates.Count, totalTracks / 2, totalTracks + 1);
        }
        else
        {
            // Large playlists: Should limit to ~20 updates regardless of size
            Assert.InRange(progressUpdates.Count, 15, 30);
        }

        // 2. Verify progress increments are reasonable
        var progressValues = progressUpdates.Select(u => u.progress).ToList();
        Assert.Equal(0, progressValues.First());  // Starts at 0%
        Assert.Equal(100, progressValues.Last()); // Ends at 100%

        // 3. Verify progress is monotonically increasing
        for (int i = 1; i < progressValues.Count; i++)
        {
            Assert.True(progressValues[i] >= progressValues[i - 1], 
                       $"Progress should be non-decreasing. Found {progressValues[i - 1]}% -> {progressValues[i]}%");
        }

        // 4. Verify batch messages are meaningful
        var batchMessages = progressUpdates.Select(u => u.batch).ToList();
        Assert.All(batchMessages, batch => Assert.False(string.IsNullOrWhiteSpace(batch)));

        // 5. Verify expected batch size calculation
        var expectedBatchSize = Math.Max(1, totalTracks / 20);
        Assert.Equal(expectedBatchSize, reporter.BatchSize);

        // 6. Verify total expected updates calculation
        var expectedUpdates = (totalTracks - 1) / expectedBatchSize + 1;
        Assert.Equal(expectedUpdates, reporter.TotalExpectedUpdates);
    }

    [Fact]
    public void SmartProgressReporter_CompareEfficiencyAcrossSizes()
    {
        // Arrange - Test different playlist sizes
        var testSizes = new[] { 20, 100, 500, 1000, 2000 };
        var results = new Dictionary<int, (int updates, int batchSize, double efficiency)>();

        foreach (var size in testSizes)
        {
            // Act
            var reporter = new SmartProgressReporter(size);
            var updateCount = 0;

            for (int track = 0; track <= size; track++)
            {
                if (reporter.ShouldReportProgress(track))
                {
                    reporter.CreateUpdate(track);
                    updateCount++;
                }
            }

            // Calculate efficiency (fewer updates per track is more efficient for large playlists)
            var efficiency = (double)updateCount / size;
            results[size] = (updateCount, reporter.BatchSize, efficiency);
        }

        // Assert - Verify efficiency improves with playlist size
        var efficiencyValues = results.Values.Select(r => r.efficiency).ToList();
        
        // For large playlists, efficiency should be better (fewer updates per track)
        var smallPlaylistEfficiency = results[20].efficiency;
        var largePlaylistEfficiency = results[1000].efficiency;
        
        Assert.True(largePlaylistEfficiency < smallPlaylistEfficiency, 
                   $"Large playlists should be more efficient. " +
                   $"Small: {smallPlaylistEfficiency:F4} updates/track, " +
                   $"Large: {largePlaylistEfficiency:F4} updates/track");

        // Verify batch sizes scale appropriately
        Assert.True(results[1000].batchSize > results[100].batchSize);
        Assert.True(results[100].batchSize > results[20].batchSize);
    }

    [Theory]
    [InlineData(20, 5)]    // 20 tracks, expect 5% increments
    [InlineData(100, 5)]   // 100 tracks, expect 5% increments  
    [InlineData(500, 5)]   // 500 tracks, expect 5% increments
    [InlineData(1000, 5)]  // 1000 tracks, expect 5% increments
    public void SmartProgressReporter_MaintainsConsistentProgressIncrements(int totalTracks, int expectedIncrement)
    {
        // Arrange
        var reporter = new SmartProgressReporter(totalTracks);
        var progressValues = new List<int>();

        // Act
        for (int track = 0; track <= totalTracks; track++)
        {
            if (reporter.ShouldReportProgress(track))
            {
                var update = reporter.CreateUpdate(track);
                progressValues.Add(update.Progress);
            }
        }

        // Assert - Verify consistent increments
        var uniqueProgressValues = progressValues.Distinct().OrderBy(p => p).ToList();
        
        // Should have progress updates approximately every 5%
        var incrementCounts = new Dictionary<int, int>();
        for (int i = 1; i < uniqueProgressValues.Count; i++)
        {
            var increment = uniqueProgressValues[i] - uniqueProgressValues[i - 1];
            incrementCounts[increment] = incrementCounts.GetValueOrDefault(increment) + 1;
        }

        // Most increments should be around the expected value (allowing some variance)
        var mostCommonIncrement = incrementCounts.OrderByDescending(kvp => kvp.Value).First().Key;
        Assert.InRange(mostCommonIncrement, expectedIncrement - 2, expectedIncrement + 2);
    }

    [Fact]
    public void SmartProgressReporter_PerformanceWithExtremelyLargePlaylist()
    {
        // Arrange - Test with very large playlist
        const int extremeSize = 10000;
        var reporter = new SmartProgressReporter(extremeSize);
        var updateCount = 0;
        var startTime = DateTime.UtcNow;

        // Act
        for (int track = 0; track <= extremeSize; track++)
        {
            if (reporter.ShouldReportProgress(track))
            {
                reporter.CreateUpdate(track, $"Track {track}");
                updateCount++;
            }
        }

        var processingTime = DateTime.UtcNow - startTime;

        // Assert
        // Should still limit updates even for extremely large playlists
        Assert.InRange(updateCount, 15, 30);
        
        // Batch size should be proportional
        var expectedBatchSize = extremeSize / 20; // 500 tracks per batch
        Assert.Equal(expectedBatchSize, reporter.BatchSize);

        // Performance should be reasonable (progress reporting shouldn't be a bottleneck)
        Assert.True(processingTime.TotalMilliseconds < 1000, 
                   $"Progress reporting took too long: {processingTime.TotalMilliseconds}ms");
    }

    [Theory]
    [InlineData(1)]      // Edge case: single track
    [InlineData(2)]      // Edge case: two tracks
    [InlineData(3)]      // Edge case: three tracks
    public void SmartProgressReporter_HandlesVerySmallPlaylists(int totalTracks)
    {
        // Arrange
        var reporter = new SmartProgressReporter(totalTracks);
        var updateCount = 0;

        // Act
        for (int track = 0; track <= totalTracks; track++)
        {
            if (reporter.ShouldReportProgress(track))
            {
                var update = reporter.CreateUpdate(track);
                updateCount++;
                
                // Verify each update is valid
                Assert.InRange(update.Progress, 0, 100);
                Assert.Equal(track, update.ProcessedTracks);
                Assert.Equal(totalTracks, update.TotalTracks);
                Assert.False(string.IsNullOrWhiteSpace(update.CurrentBatch));
                Assert.False(string.IsNullOrWhiteSpace(update.Message));
            }
        }

        // Assert
        // For very small playlists, should report every track
        Assert.Equal(totalTracks + 1, updateCount); // 0 to totalTracks inclusive

        // Batch size should be 1 for very small playlists
        Assert.Equal(1, reporter.BatchSize);
    }

    [Fact]
    public void SmartProgressReporter_NetworkEfficiencyAnalysis()
    {
        // Arrange - Compare old fixed batching vs new smart batching
        var testSizes = new[] { 50, 100, 500, 1000, 2000 };
        
        foreach (var size in testSizes)
        {
            // Old approach: update every 10 tracks
            var oldApproachUpdates = (size / 10) + 2; // +2 for start and end
            
            // New approach: smart batching
            var reporter = new SmartProgressReporter(size);
            var newApproachUpdates = reporter.TotalExpectedUpdates;
            
            // Calculate efficiency improvement
            var improvement = (oldApproachUpdates - newApproachUpdates) / (double)oldApproachUpdates * 100;
            
            // Assert - New approach should be more efficient for large playlists
            if (size >= 200)
            {
                Assert.True(improvement > 0, 
                           $"For {size} tracks: Old approach: {oldApproachUpdates} updates, " +
                           $"New approach: {newApproachUpdates} updates. " +
                           $"Expected improvement, got {improvement:F1}%");
            }
            
            // Output for verification
            Console.WriteLine($"Playlist size {size}: Old={oldApproachUpdates}, New={newApproachUpdates}, Improvement={improvement:F1}%");
        }
    }
}