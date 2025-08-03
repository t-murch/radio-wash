namespace RadioWash.Api.Models;

public class ProgressUpdate
{
    public int Progress { get; set; }
    public int ProcessedTracks { get; set; }
    public int TotalTracks { get; set; }
    public string CurrentBatch { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}