using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Factory for creating playlist cleaners
/// </summary>
public class PlaylistCleanerFactory : IPlaylistCleanerFactory
{
    private readonly IServiceProvider _serviceProvider;

    public PlaylistCleanerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IPlaylistCleaner CreateCleaner(string platform)
    {
        return platform.ToLower() switch
        {
            "spotify" => _serviceProvider.GetRequiredService<SpotifyPlaylistCleaner>(),
            _ => throw new NotSupportedException($"Platform '{platform}' is not supported")
        };
    }
}