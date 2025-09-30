using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Logging;

public static class SyncLoggingExtensions
{
  private static readonly Action<ILogger, int, int, Exception?> _syncStarted =
    LoggerMessage.Define<int, int>(
      LogLevel.Information,
      new EventId(1001, "SyncStarted"),
      "Sync started for config {ConfigId}, user {UserId}");

  private static readonly Action<ILogger, int, int, int, int, long, Exception?> _syncCompleted =
    LoggerMessage.Define<int, int, int, int, long>(
      LogLevel.Information,
      new EventId(1002, "SyncCompleted"),
      "Sync completed for config {ConfigId}, user {UserId}. Added: {TracksAdded}, Removed: {TracksRemoved}, Duration: {DurationMs}ms");

  private static readonly Action<ILogger, int, int, long, string, Exception?> _syncFailed =
    LoggerMessage.Define<int, int, long, string>(
      LogLevel.Error,
      new EventId(1003, "SyncFailed"),
      "Sync failed for config {ConfigId}, user {UserId}. Duration: {DurationMs}ms, Error: {ErrorMessage}");

  private static readonly Action<ILogger, int, int, int, Exception?> _newTracksProcessing =
    LoggerMessage.Define<int, int, int>(
      LogLevel.Information,
      new EventId(1004, "NewTracksProcessing"),
      "Processing {NewTrackCount} new tracks for config {ConfigId}, user {UserId}");

  private static readonly Action<ILogger, string, int, string, Exception?> _playlistOperation =
    LoggerMessage.Define<string, int, string>(
      LogLevel.Information,
      new EventId(1005, "PlaylistOperation"),
      "Playlist operation: {Operation} - {TrackCount} tracks for playlist {PlaylistId}");

  private static readonly Action<ILogger, string, string, Exception?> _trackProcessingFailed =
    LoggerMessage.Define<string, string>(
      LogLevel.Warning,
      new EventId(1006, "TrackProcessingFailed"),
      "Failed to process track {TrackId} ({TrackName})");

  private static readonly Action<ILogger, int, Exception?> _subscriptionValidationStarted =
    LoggerMessage.Define<int>(
      LogLevel.Information,
      new EventId(1007, "SubscriptionValidationStarted"),
      "Starting subscription validation for user {UserId}");

  private static readonly Action<ILogger, int, bool, Exception?> _subscriptionValidationCompleted =
    LoggerMessage.Define<int, bool>(
      LogLevel.Information,
      new EventId(1008, "SubscriptionValidationCompleted"),
      "Subscription validation completed for user {UserId}. Valid: {IsValid}");

  private static readonly Action<ILogger, int, int, string, Exception?> _syncConfigEnabled =
    LoggerMessage.Define<int, int, string>(
      LogLevel.Information,
      new EventId(1009, "SyncConfigEnabled"),
      "Sync enabled for job {JobId}, user {UserId}, frequency: {Frequency}");

  private static readonly Action<ILogger, int, int, Exception?> _syncConfigDisabled =
    LoggerMessage.Define<int, int>(
      LogLevel.Information,
      new EventId(1010, "SyncConfigDisabled"),
      "Sync disabled for config {ConfigId}, user {UserId}");

  private static readonly Action<ILogger, int, int, string, Exception?> _manualSyncRequested =
    LoggerMessage.Define<int, int, string>(
      LogLevel.Information,
      new EventId(1011, "ManualSyncRequested"),
      "Manual sync requested for config {ConfigId}, user {UserId}, trigger: {Trigger}");

  private static readonly Action<ILogger, string, int, Exception?> _batchOperationStarted =
    LoggerMessage.Define<string, int>(
      LogLevel.Information,
      new EventId(1012, "BatchOperationStarted"),
      "Starting batch operation: {Operation} for {Count} items");

  private static readonly Action<ILogger, string, int, long, Exception?> _batchOperationCompleted =
    LoggerMessage.Define<string, int, long>(
      LogLevel.Information,
      new EventId(1013, "BatchOperationCompleted"),
      "Batch operation completed: {Operation} for {Count} items in {DurationMs}ms");

  public static void LogSyncStarted(this ILogger logger, int configId, int userId)
    => _syncStarted(logger, configId, userId, null);

  public static void LogSyncCompleted(this ILogger logger, int configId, int userId, int tracksAdded, int tracksRemoved, long durationMs)
    => _syncCompleted(logger, configId, userId, tracksAdded, tracksRemoved, durationMs, null);

  public static void LogSyncFailed(this ILogger logger, int configId, int userId, long durationMs, string errorMessage, Exception? exception = null)
    => _syncFailed(logger, configId, userId, durationMs, errorMessage, exception);

  public static void LogNewTracksProcessing(this ILogger logger, int newTrackCount, int configId, int userId)
    => _newTracksProcessing(logger, newTrackCount, configId, userId, null);

  public static void LogPlaylistOperation(this ILogger logger, string operation, int trackCount, string playlistId)
    => _playlistOperation(logger, operation, trackCount, playlistId, null);

  public static void LogTrackProcessingFailed(this ILogger logger, string trackId, string trackName, Exception? exception = null)
    => _trackProcessingFailed(logger, trackId, trackName, exception);

  public static void LogSubscriptionValidationStarted(this ILogger logger, int userId)
    => _subscriptionValidationStarted(logger, userId, null);

  public static void LogSubscriptionValidationCompleted(this ILogger logger, int userId, bool isValid)
    => _subscriptionValidationCompleted(logger, userId, isValid, null);

  public static void LogSyncConfigEnabled(this ILogger logger, int jobId, int userId, string frequency)
    => _syncConfigEnabled(logger, jobId, userId, frequency, null);

  public static void LogSyncConfigDisabled(this ILogger logger, int configId, int userId)
    => _syncConfigDisabled(logger, configId, userId, null);

  public static void LogManualSyncRequested(this ILogger logger, int configId, int userId, string trigger = "user")
    => _manualSyncRequested(logger, configId, userId, trigger, null);

  public static void LogBatchOperationStarted(this ILogger logger, string operation, int count)
    => _batchOperationStarted(logger, operation, count, null);

  public static void LogBatchOperationCompleted(this ILogger logger, string operation, int count, long durationMs)
    => _batchOperationCompleted(logger, operation, count, durationMs, null);

  public static IDisposable? BeginSyncScope(this ILogger logger, int configId, int userId, string operation)
  {
    return logger.BeginScope(new Dictionary<string, object>
    {
      ["ConfigId"] = configId,
      ["UserId"] = userId,
      ["Operation"] = operation,
      ["CorrelationId"] = Guid.NewGuid().ToString()
    });
  }

  public static IDisposable? BeginBatchScope(this ILogger logger, string batchOperation, int itemCount)
  {
    return logger.BeginScope(new Dictionary<string, object>
    {
      ["BatchOperation"] = batchOperation,
      ["ItemCount"] = itemCount,
      ["BatchId"] = Guid.NewGuid().ToString()
    });
  }

  public static string GetErrorType(Exception exception)
  {
    return exception switch
    {
      InvalidOperationException => "InvalidOperation",
      UnauthorizedAccessException => "Unauthorized",
      HttpRequestException => "HttpRequest",
      TimeoutException => "Timeout",
      ArgumentException => "InvalidArgument",
      _ => exception.GetType().Name
    };
  }
}