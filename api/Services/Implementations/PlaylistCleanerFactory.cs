using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Factory for creating playlist cleaners. Resolves the keyed <see cref="IMusicService"/>
/// registered for the requested provider, so adding a platform means one keyed DI
/// registration — no factory edits.
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
    string provider;
    try
    {
      provider = MusicProviders.NormalizeOrThrow(platform);
    }
    catch (ArgumentException ex)
    {
      throw new NotSupportedException($"Platform '{platform}' is not supported", ex);
    }

    var musicService = _serviceProvider.GetRequiredKeyedService<IMusicService>(provider);
    return ActivatorUtilities.CreateInstance<PlaylistCleaner>(_serviceProvider, musicService);
  }
}
