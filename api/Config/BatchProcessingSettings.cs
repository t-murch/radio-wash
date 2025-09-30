namespace RadioWash.Api.Configuration;

/// <summary>
/// Configuration settings for batch processing
/// </summary>
public class BatchProcessingSettings
{
  public const string SectionName = "BatchProcessing";

  public int BatchSize { get; set; } = 100;
  public int ProgressReportingThreshold { get; set; } = 5;
  public int DatabasePersistenceThreshold { get; set; } = 10;
}
