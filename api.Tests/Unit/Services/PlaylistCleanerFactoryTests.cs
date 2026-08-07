using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Tests for the keyed-DI-driven PlaylistCleanerFactory. Contracts: every supported provider
/// resolves to a cleaner wired to that provider's keyed IMusicService; unknown providers and
/// providers without a keyed registration fail loudly.
/// </summary>
public class PlaylistCleanerFactoryTests
{
  private static ServiceProvider BuildServiceProvider(params (string key, IMusicService service)[] keyedServices)
  {
    var services = new ServiceCollection();
    services.AddSingleton(new Mock<IProgressTracker>().Object);
    services.AddSingleton(new Mock<IProgressBroadcastService>().Object);
    services.AddSingleton(new Mock<IUnitOfWork>().Object);
    services.AddSingleton(new Mock<ILogger<PlaylistCleaner>>().Object);
    foreach (var (key, service) in keyedServices)
    {
      services.AddKeyedSingleton(key, service);
    }
    return services.BuildServiceProvider();
  }

  [Theory]
  [InlineData("apple_music")]
  [InlineData("APPLE_MUSIC")]
  [InlineData("  Apple_Music  ")]
  public void CreateCleaner_WithSupportedProvider_ResolvesCleaner(string platform)
  {
    var key = MusicProviders.NormalizeOrThrow(platform);
    using var sp = BuildServiceProvider((key, new Mock<IMusicService>().Object));
    var factory = new PlaylistCleanerFactory(sp);

    var cleaner = factory.CreateCleaner(platform);

    Assert.IsType<PlaylistCleaner>(cleaner);
  }

  [Fact]
  public void CreateCleaner_WithUnsupportedProvider_ThrowsNotSupported()
  {
    using var sp = BuildServiceProvider();
    var factory = new PlaylistCleanerFactory(sp);

    var ex = Assert.Throws<NotSupportedException>(() => factory.CreateCleaner("tidal"));
    Assert.Contains("tidal", ex.Message);
  }

  [Fact]
  public void CreateCleaner_WithSupportedProviderButNoRegistration_Throws()
  {
    // apple_music is on the allowlist but has no keyed registration in this container;
    // the factory must fail loudly instead of silently falling back to another provider.
    using var sp = BuildServiceProvider();
    var factory = new PlaylistCleanerFactory(sp);

    Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateCleaner("apple_music"));
  }
}
