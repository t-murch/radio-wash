namespace RadioWash.Api.Models.Domain;

public class PlaylistSyncHistory
{
    public int Id { get; set; }
    public int SyncConfigId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = SyncStatus.Pending;
    public int TracksAdded { get; set; } = 0;
    public int TracksRemoved { get; set; } = 0;
    public int TracksUnchanged { get; set; } = 0;
    public string? ErrorMessage { get; set; }
    public int? ExecutionTimeMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public PlaylistSyncConfig SyncConfig { get; set; } = null!;
}