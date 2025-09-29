namespace RadioWash.Api.Models.Domain;

public static class SyncFrequency
{
    public const string Daily = "daily"; // Changed from daily_4x to daily (once per day at 00:01)
    public const string Weekly = "weekly";
    public const string Manual = "manual";
}

public static class SyncStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public class PlaylistSyncConfig
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int OriginalJobId { get; set; }
    public string SourcePlaylistId { get; set; } = null!;
    public string TargetPlaylistId { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public string SyncFrequency { get; set; } = "daily"; // Default daily at 00:01
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncStatus { get; set; }
    public string? LastSyncError { get; set; }
    public DateTime? NextScheduledSync { get; set; }
    public string? SyncStats { get; set; } // JSON stats
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User User { get; set; } = null!;
    public CleanPlaylistJob OriginalJob { get; set; } = null!;
    public ICollection<PlaylistSyncHistory> SyncHistory { get; set; } = new List<PlaylistSyncHistory>();
}