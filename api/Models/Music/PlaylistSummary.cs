namespace RadioWash.Api.Models.Music;

/// <summary>
/// Provider-agnostic playlist summary. Used for both listing a user's playlists and
/// representing a newly created target playlist. Only the fields the cleaner (or the UI on
/// the playlist-list endpoint) actually reads.
/// </summary>
public record PlaylistSummary(
    string Id,
    string Name,
    string? Description,
    string? ImageUrl,
    int TrackCount,
    string OwnerId,
    string? OwnerName);
