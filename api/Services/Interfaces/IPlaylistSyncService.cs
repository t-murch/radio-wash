using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

public class PlaylistSyncResult
{
  public bool Success { get; set; }
  public int TracksAdded { get; set; }
  public int TracksRemoved { get; set; }
  public int TracksUnchanged { get; set; }
  public string? ErrorMessage { get; set; }
  public TimeSpan ExecutionTime { get; set; }
}

public interface IPlaylistSyncService
{
  Task<PlaylistSyncResult> SyncPlaylistAsync(PlaylistSyncConfig config);
  Task<PlaylistSyncConfig?> EnableSyncForJobAsync(int jobId, int userId);
  Task<bool> DisableSyncAsync(int syncConfigId, int userId);
  Task<IEnumerable<PlaylistSyncConfig>> GetUserSyncConfigsAsync(int userId);
  Task<PlaylistSyncConfig?> UpdateSyncFrequencyAsync(int syncConfigId, string frequency, int userId);
  Task<PlaylistSyncResult> ManualSyncAsync(int syncConfigId, int userId);
  Task<IEnumerable<PlaylistSyncHistory>> GetSyncHistoryAsync(int syncConfigId, int limit = 20);
}
