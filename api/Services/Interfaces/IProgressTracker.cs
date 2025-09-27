using RadioWash.Api.Models;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Abstraction for progress tracking
/// </summary>
public interface IProgressTracker
{
    void Initialize(int totalItems, BatchConfiguration config);
    bool ShouldReportProgress(int currentItem);
    bool ShouldPersistProgress(int currentItem);
    ProgressUpdate CreateUpdate(int currentItem, string? currentItemName = null);
}