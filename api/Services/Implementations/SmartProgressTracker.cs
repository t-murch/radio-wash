using RadioWash.Api.Models;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Smart progress tracker implementation
/// </summary>
public class SmartProgressTracker : IProgressTracker
{
    private int _totalItems;
    private BatchConfiguration _config = null!;
    private int _lastReportedProgress;
    private int _lastPersistedProgress;

    public void Initialize(int totalItems, BatchConfiguration config)
    {
        _totalItems = totalItems;
        _config = config;
        _lastReportedProgress = 0;
        _lastPersistedProgress = 0;
    }

    public bool ShouldReportProgress(int currentItem)
    {
        var currentProgress = GetProgressPercentage(currentItem);
        if (currentProgress - _lastReportedProgress >= _config.ProgressReportingThreshold)
        {
            _lastReportedProgress = currentProgress;
            return true;
        }
        return currentItem == _totalItems;
    }

    public bool ShouldPersistProgress(int currentItem)
    {
        var currentProgress = GetProgressPercentage(currentItem);
        if (currentProgress - _lastPersistedProgress >= _config.DatabasePersistenceThreshold)
        {
            _lastPersistedProgress = currentProgress;
            return true;
        }
        return currentItem == _totalItems;
    }

    public ProgressUpdate CreateUpdate(int currentItem, string? currentItemName = null)
    {
        var progress = GetProgressPercentage(currentItem);
        return new ProgressUpdate
        {
            Progress = progress,
            ProcessedTracks = currentItem,
            TotalTracks = _totalItems,
            CurrentBatch = $"Batch {(currentItem / _config.BatchSize) + 1}",
            Message = currentItemName ?? ""
        };
    }

    private int GetProgressPercentage(int currentItem)
    {
        return _totalItems > 0 ? (currentItem * 100) / _totalItems : 0;
    }
}