namespace RadioWash.Api.Models.Music;

/// <summary>
/// Provider-agnostic representation of a single track. Thin by design — carries only the
/// fields the platform-independent cleaner loop reads. Do not extend this to mirror every
/// Spotify or Apple Music field; extend only when a cleaner truly needs that data.
/// </summary>
public record MusicTrack(
    string Id,
    string Name,
    bool IsExplicit,
    IReadOnlyList<MusicArtist> Artists);

public record MusicArtist(string Name);
