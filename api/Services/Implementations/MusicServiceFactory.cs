using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class MusicServiceFactory : IMusicServiceFactory
{
  private readonly IServiceProvider _serviceProvider;

  public MusicServiceFactory(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public IMusicService GetService(string provider)
  {
    var normalized = MusicProviders.NormalizeOrThrow(provider);
    return _serviceProvider.GetRequiredKeyedService<IMusicService>(normalized);
  }
}
