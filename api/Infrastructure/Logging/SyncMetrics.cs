using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RadioWash.Api.Infrastructure.Logging;

public class SyncMetrics : IDisposable
{
  private readonly Meter _meter;
  private readonly Counter<int> _syncStartedCounter;
  private readonly Counter<int> _syncCompletedCounter;
  private readonly Counter<int> _syncFailedCounter;
  private readonly Histogram<long> _syncDurationHistogram;
  private readonly Counter<int> _tracksProcessedCounter;
  private readonly Counter<int> _tracksAddedCounter;
  private readonly Counter<int> _tracksRemovedCounter;
  private readonly Counter<int> _subscriptionValidationCounter;
  private readonly UpDownCounter<int> _activeSyncConfigsGauge;
  private readonly ConcurrentDictionary<string, int> _activeSyncConfigs = new();

  public SyncMetrics()
  {
    _meter = new Meter("RadioWash.Sync");
    
    // Sync operation counters
    _syncStartedCounter = _meter.CreateCounter<int>(
      "sync_operations_started_total",
      description: "Total number of sync operations started");

    _syncCompletedCounter = _meter.CreateCounter<int>(
      "sync_operations_completed_total",
      description: "Total number of sync operations completed successfully");

    _syncFailedCounter = _meter.CreateCounter<int>(
      "sync_operations_failed_total",
      description: "Total number of sync operations that failed");

    // Sync performance metrics
    _syncDurationHistogram = _meter.CreateHistogram<long>(
      "sync_duration_milliseconds",
      unit: "ms",
      description: "Duration of sync operations in milliseconds");

    // Track processing metrics
    _tracksProcessedCounter = _meter.CreateCounter<int>(
      "tracks_processed_total",
      description: "Total number of tracks processed during sync operations");

    _tracksAddedCounter = _meter.CreateCounter<int>(
      "tracks_added_total",
      description: "Total number of tracks added to playlists");

    _tracksRemovedCounter = _meter.CreateCounter<int>(
      "tracks_removed_total",
      description: "Total number of tracks removed from playlists");

    // Subscription metrics
    _subscriptionValidationCounter = _meter.CreateCounter<int>(
      "subscription_validations_total",
      description: "Total number of subscription validations performed");

    // Active sync configurations gauge
    _activeSyncConfigsGauge = _meter.CreateUpDownCounter<int>(
      "active_sync_configs",
      description: "Number of active sync configurations");
  }

  public void RecordSyncStarted(int configId, int userId, string frequency)
  {
    _syncStartedCounter.Add(1, new KeyValuePair<string, object?>("user_id", userId.ToString()),
      new KeyValuePair<string, object?>("frequency", frequency));
    
    var key = $"{configId}:{userId}";
    if (_activeSyncConfigs.TryAdd(key, 1))
    {
      _activeSyncConfigsGauge.Add(1);
    }
  }

  public void RecordSyncCompleted(int configId, int userId, long durationMs, int tracksAdded, int tracksRemoved, string frequency)
  {
    _syncCompletedCounter.Add(1, new KeyValuePair<string, object?>("user_id", userId.ToString()),
      new KeyValuePair<string, object?>("frequency", frequency));
    
    _syncDurationHistogram.Record(durationMs, new KeyValuePair<string, object?>("user_id", userId.ToString()),
      new KeyValuePair<string, object?>("frequency", frequency),
      new KeyValuePair<string, object?>("status", "completed"));

    _tracksAddedCounter.Add(tracksAdded, new KeyValuePair<string, object?>("user_id", userId.ToString()));
    _tracksRemovedCounter.Add(tracksRemoved, new KeyValuePair<string, object?>("user_id", userId.ToString()));
    
    var key = $"{configId}:{userId}";
    if (_activeSyncConfigs.TryRemove(key, out _))
    {
      _activeSyncConfigsGauge.Add(-1);
    }
  }

  public void RecordSyncFailed(int configId, int userId, long durationMs, string errorType, string frequency)
  {
    _syncFailedCounter.Add(1, new KeyValuePair<string, object?>("user_id", userId.ToString()),
      new KeyValuePair<string, object?>("error_type", errorType),
      new KeyValuePair<string, object?>("frequency", frequency));
    
    _syncDurationHistogram.Record(durationMs, new KeyValuePair<string, object?>("user_id", userId.ToString()),
      new KeyValuePair<string, object?>("frequency", frequency),
      new KeyValuePair<string, object?>("status", "failed"));
    
    var key = $"{configId}:{userId}";
    if (_activeSyncConfigs.TryRemove(key, out _))
    {
      _activeSyncConfigsGauge.Add(-1);
    }
  }

  public void RecordTracksProcessed(int count, int userId)
  {
    _tracksProcessedCounter.Add(count, new KeyValuePair<string, object?>("user_id", userId.ToString()));
  }

  public void RecordSubscriptionValidation(int userId, bool isValid)
  {
    _subscriptionValidationCounter.Add(1, new KeyValuePair<string, object?>("user_id", userId.ToString()),
      new KeyValuePair<string, object?>("is_valid", isValid.ToString()));
  }

  public void Dispose()
  {
    _meter?.Dispose();
  }
}