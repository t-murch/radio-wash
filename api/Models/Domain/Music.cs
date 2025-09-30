namespace RadioWash.Api.Models.Domain;

/// <summary>
/// Value object representing the progress of a job
/// </summary>
public class JobProgress
{
  public int ProcessedTracks { get; }
  public int TotalTracks { get; }
  public int PercentComplete => TotalTracks > 0 ? (ProcessedTracks * 100) / TotalTracks : 0;
  public string CurrentBatch { get; }
  public int MatchedTracks { get; }

  public JobProgress(int processedTracks, int totalTracks, string currentBatch, int matchedTracks)
  {
    ProcessedTracks = processedTracks;
    TotalTracks = totalTracks;
    CurrentBatch = currentBatch;
    MatchedTracks = matchedTracks;
  }
}

/// <summary>
/// Value object for batch processing configuration
/// </summary>
public class BatchConfiguration
{
  public int BatchSize { get; }
  public int ProgressReportingThreshold { get; }
  public int DatabasePersistenceThreshold { get; }

  public BatchConfiguration(int batchSize = 100, int progressThreshold = 5, int dbThreshold = 10)
  {
    BatchSize = batchSize;
    ProgressReportingThreshold = progressThreshold;
    DatabasePersistenceThreshold = dbThreshold;
  }
}

/// <summary>
/// Result models for track and playlist processing
/// </summary>
public class PlaylistCleaningResult
{
  public int ProcessedTracks { get; set; }
  public int MatchedTracks { get; set; }
  public string TargetPlaylistId { get; set; } = string.Empty;
  public List<string> CleanTrackUris { get; set; } = new();
}

public class TrackProcessingResult
{
  public int ProcessedCount { get; set; }
  public int MatchedCount { get; set; }
  public List<string> CleanTrackUris { get; set; } = new();
}
