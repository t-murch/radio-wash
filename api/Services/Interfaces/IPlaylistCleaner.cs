using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Factory pattern for creating playlist cleaning strategies
/// </summary>
public interface IPlaylistCleanerFactory
{
    IPlaylistCleaner CreateCleaner(string platform);
}

/// <summary>
/// Strategy pattern for playlist cleaning logic
/// </summary>
public interface IPlaylistCleaner
{
    Task<PlaylistCleaningResult> CleanPlaylistAsync(CleanPlaylistJob job, User user);
}