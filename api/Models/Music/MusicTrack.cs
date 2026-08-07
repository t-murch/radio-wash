namespace RadioWash.Api.Models.Music;

/// <summary>
/// Provider-agnostic representation of a single track. Thin by design — carries only the
/// fields the platform-independent cleaner loop reads. Do not extend this to mirror every
/// Apple Music field; extend only when a cleaner truly needs that data.
/// The optional tail (Isrc, DurationMs, AlbumName) exists for the cross-service copy
/// pipeline: ISRC is the cross-catalog bridge, duration and album disambiguate search
/// fallback matches.
/// </summary>
public record MusicTrack(
    string Id,
    string Name,
    bool IsExplicit,
    IReadOnlyList<MusicArtist> Artists,
    string? Isrc = null,
    int? DurationMs = null,
    string? AlbumName = null);

public record MusicArtist(string Name);
