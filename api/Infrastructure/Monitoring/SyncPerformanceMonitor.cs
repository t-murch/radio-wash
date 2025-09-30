using System.Collections.Concurrent;
using System.Diagnostics;
using RadioWash.Api.Infrastructure.Logging;

namespace RadioWash.Api.Infrastructure.Monitoring;

public interface ISyncPerformanceMonitor
{
  IDisposable BeginSyncOperation(int configId, int userId, string operation);
  void RecordSpotifyApiCall(string endpoint, long durationMs, bool success);
  void RecordDatabaseOperation(string operation, long durationMs, int recordCount = 0);
  void RecordTrackProcessingTime(long durationMs, bool foundCleanVersion);
  SyncPerformanceStats GetCurrentStats();
  void Reset();
}

public class SyncPerformanceMonitor : ISyncPerformanceMonitor
{
  private readonly SyncMetrics _metrics;
  private readonly ILogger<SyncPerformanceMonitor> _logger;
  private readonly ConcurrentDictionary<string, OperationTracker> _activeOperations = new();
  private readonly SyncPerformanceStats _stats = new();

  public SyncPerformanceMonitor(SyncMetrics metrics, ILogger<SyncPerformanceMonitor> logger)
  {
    _metrics = metrics;
    _logger = logger;
  }

  public IDisposable BeginSyncOperation(int configId, int userId, string operation)
  {
    var operationId = $"{configId}:{userId}:{operation}:{Guid.NewGuid():N}";
    var tracker = new OperationTracker(operationId, configId, userId, operation, this);
    _activeOperations.TryAdd(operationId, tracker);
    
    _logger.LogDebug("Started performance tracking for operation {OperationId}: {Operation}", operationId, operation);
    return tracker;
  }

  public void RecordSpotifyApiCall(string endpoint, long durationMs, bool success)
  {
    Interlocked.Increment(ref _stats.SpotifyApiCalls);
    Interlocked.Add(ref _stats.TotalSpotifyApiTime, durationMs);
    
    if (!success)
    {
      Interlocked.Increment(ref _stats.FailedSpotifyApiCalls);
    }

    _logger.LogDebug("Spotify API call to {Endpoint}: {DurationMs}ms, Success: {Success}", endpoint, durationMs, success);
  }

  public void RecordDatabaseOperation(string operation, long durationMs, int recordCount = 0)
  {
    Interlocked.Increment(ref _stats.DatabaseOperations);
    Interlocked.Add(ref _stats.TotalDatabaseTime, durationMs);
    
    if (recordCount > 0)
    {
      Interlocked.Add(ref _stats.RecordsProcessed, recordCount);
    }

    _logger.LogDebug("Database operation {Operation}: {DurationMs}ms, Records: {RecordCount}", operation, durationMs, recordCount);
  }

  public void RecordTrackProcessingTime(long durationMs, bool foundCleanVersion)
  {
    Interlocked.Increment(ref _stats.TracksProcessed);
    Interlocked.Add(ref _stats.TotalTrackProcessingTime, durationMs);
    
    if (foundCleanVersion)
    {
      Interlocked.Increment(ref _stats.CleanVersionsFound);
    }

    _logger.LogDebug("Track processing: {DurationMs}ms, Clean version found: {FoundClean}", durationMs, foundCleanVersion);
  }

  public SyncPerformanceStats GetCurrentStats()
  {
    return new SyncPerformanceStats
    {
      SpotifyApiCalls = _stats.SpotifyApiCalls,
      FailedSpotifyApiCalls = _stats.FailedSpotifyApiCalls,
      TotalSpotifyApiTime = _stats.TotalSpotifyApiTime,
      DatabaseOperations = _stats.DatabaseOperations,
      TotalDatabaseTime = _stats.TotalDatabaseTime,
      RecordsProcessed = _stats.RecordsProcessed,
      TracksProcessed = _stats.TracksProcessed,
      CleanVersionsFound = _stats.CleanVersionsFound,
      TotalTrackProcessingTime = _stats.TotalTrackProcessingTime,
      ActiveOperations = _activeOperations.Count
    };
  }

  public void Reset()
  {
    _stats.Reset();
    _activeOperations.Clear();
    _logger.LogDebug("Performance monitor stats reset");
  }

  internal void CompleteOperation(string operationId, long durationMs, bool success)
  {
    if (_activeOperations.TryRemove(operationId, out var tracker))
    {
      _logger.LogDebug("Completed performance tracking for operation {OperationId}: {DurationMs}ms, Success: {Success}", 
        operationId, durationMs, success);
    }
  }

  private class OperationTracker : IDisposable
  {
    private readonly string _operationId;
    private readonly int _configId;
    private readonly int _userId;
    private readonly string _operation;
    private readonly SyncPerformanceMonitor _monitor;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    public OperationTracker(string operationId, int configId, int userId, string operation, SyncPerformanceMonitor monitor)
    {
      _operationId = operationId;
      _configId = configId;
      _userId = userId;
      _operation = operation;
      _monitor = monitor;
      _stopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
      if (_disposed) return;
      
      _stopwatch.Stop();
      _monitor.CompleteOperation(_operationId, _stopwatch.ElapsedMilliseconds, true);
      _disposed = true;
    }
  }
}

public class SyncPerformanceStats
{
  public long SpotifyApiCalls;
  public long FailedSpotifyApiCalls;
  public long TotalSpotifyApiTime;
  public long DatabaseOperations;
  public long TotalDatabaseTime;
  public long RecordsProcessed;
  public long TracksProcessed;
  public long CleanVersionsFound;
  public long TotalTrackProcessingTime;
  public int ActiveOperations;

  public double AverageSpotifyApiTime => SpotifyApiCalls > 0 ? (double)TotalSpotifyApiTime / SpotifyApiCalls : 0;
  public double AverageDatabaseTime => DatabaseOperations > 0 ? (double)TotalDatabaseTime / DatabaseOperations : 0;
  public double AverageTrackProcessingTime => TracksProcessed > 0 ? (double)TotalTrackProcessingTime / TracksProcessed : 0;
  public double SpotifyApiSuccessRate => SpotifyApiCalls > 0 ? (double)(SpotifyApiCalls - FailedSpotifyApiCalls) / SpotifyApiCalls : 0;
  public double CleanVersionFoundRate => TracksProcessed > 0 ? (double)CleanVersionsFound / TracksProcessed : 0;

  public void Reset()
  {
    SpotifyApiCalls = 0;
    FailedSpotifyApiCalls = 0;
    TotalSpotifyApiTime = 0;
    DatabaseOperations = 0;
    TotalDatabaseTime = 0;
    RecordsProcessed = 0;
    TracksProcessed = 0;
    CleanVersionsFound = 0;
    TotalTrackProcessingTime = 0;
    ActiveOperations = 0;
  }
}