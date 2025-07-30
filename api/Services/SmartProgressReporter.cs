using RadioWash.Api.Models;

namespace RadioWash.Api.Services;

/// <summary>
/// Smart progress reporting service that provides percentage-based batching logic
/// for efficient progress updates that scale with playlist size.
/// </summary>
public class SmartProgressReporter
{
    private readonly int _totalTracks;
    private readonly int _batchSize;
    private readonly TimeSpan _maxTimeBetweenUpdates = TimeSpan.FromSeconds(10);
    private DateTime _lastUpdate = DateTime.MinValue;
    private int _lastReportedBatch = -1;

    /// <summary>
    /// Initializes a new SmartProgressReporter with intelligent batch sizing
    /// based on total track count.
    /// </summary>
    /// <param name="totalTracks">Total number of tracks to process</param>
    public SmartProgressReporter(int totalTracks)
    {
        if (totalTracks <= 0)
            throw new ArgumentException("Total tracks must be greater than zero", nameof(totalTracks));

        _totalTracks = totalTracks;
        // Calculate batch size to ensure ~20 updates regardless of playlist size (5% increments)
        _batchSize = Math.Max(1, totalTracks / 20);
    }

    /// <summary>
    /// Determines if progress should be reported based on batch boundaries,
    /// time elapsed, or special conditions (start/end).
    /// </summary>
    /// <param name="currentTrack">Current track being processed (1-based index)</param>
    /// <returns>True if progress should be reported</returns>
    public bool ShouldReportProgress(int currentTrack)
    {
        if (currentTrack < 0 || currentTrack > _totalTracks)
            throw new ArgumentOutOfRangeException(nameof(currentTrack), 
                $"Current track must be between 0 and {_totalTracks}");

        var currentBatch = currentTrack == 0 ? 0 : (currentTrack - 1) / _batchSize;
        var timeSinceUpdate = DateTime.UtcNow - _lastUpdate;

        // Report if: new batch reached OR max time exceeded OR at start/end
        return currentBatch > _lastReportedBatch ||
               timeSinceUpdate > _maxTimeBetweenUpdates ||
               currentTrack == 0 ||
               currentTrack == _totalTracks;
    }

    /// <summary>
    /// Creates a progress update with calculated percentages, batch information,
    /// and user-friendly messaging.
    /// </summary>
    /// <param name="currentTrack">Current track being processed (1-based index)</param>
    /// <param name="currentTrackName">Optional name of the current track being processed</param>
    /// <returns>A ProgressUpdate with all calculated fields</returns>
    public ProgressUpdate CreateUpdate(int currentTrack, string? currentTrackName = null)
    {
        if (currentTrack < 0 || currentTrack > _totalTracks)
            throw new ArgumentOutOfRangeException(nameof(currentTrack), 
                $"Current track must be between 0 and {_totalTracks}");

        var currentBatch = currentTrack == 0 ? 0 : (currentTrack - 1) / _batchSize;
        var progressPercent = currentTrack == 0 ? 0 : (int)((currentTrack / (double)_totalTracks) * 100);
        var totalBatches = (_totalTracks - 1) / _batchSize + 1;

        // Calculate batch range for display
        var batchStart = currentBatch * _batchSize + 1;
        var batchEnd = Math.Min((currentBatch + 1) * _batchSize, _totalTracks);

        _lastReportedBatch = currentBatch;
        _lastUpdate = DateTime.UtcNow;

        // Create contextual messages based on progress state
        string message;
        string currentBatchText;

        if (currentTrack == 0)
        {
            message = "Initializing playlist processing...";
            currentBatchText = "Starting batch 1";
        }
        else if (currentTrack == _totalTracks)
        {
            message = "Finalizing playlist creation...";
            currentBatchText = $"Completed all {totalBatches} batches";
        }
        else if (!string.IsNullOrWhiteSpace(currentTrackName))
        {
            message = $"Processing: {currentTrackName}";
            currentBatchText = $"Processing tracks {batchStart}-{batchEnd}";
        }
        else
        {
            message = $"Processing batch {currentBatch + 1} of {totalBatches}";
            currentBatchText = $"Processing tracks {batchStart}-{batchEnd}";
        }

        return new ProgressUpdate
        {
            Progress = progressPercent,
            ProcessedTracks = currentTrack,
            TotalTracks = _totalTracks,
            CurrentBatch = currentBatchText,
            Message = message
        };
    }

    /// <summary>
    /// Gets the calculated batch size for this progress reporter.
    /// Useful for external systems that need to align with batch boundaries.
    /// </summary>
    public int BatchSize => _batchSize;

    /// <summary>
    /// Gets the total number of expected progress updates for this playlist.
    /// Useful for setting up UI expectations.
    /// </summary>
    public int TotalExpectedUpdates => (_totalTracks - 1) / _batchSize + 1;
}