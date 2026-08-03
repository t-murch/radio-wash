namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Resolves the <see cref="IMusicService"/> adapter registered for a provider key.
/// The single seam through which provider-neutral services (job creation, playlist
/// browsing, cross-service copy) pick a platform at runtime.
/// </summary>
public interface IMusicServiceFactory
{
  /// <summary>
  /// Returns the adapter for the given provider. Throws <see cref="ArgumentException"/>
  /// for unsupported provider keys.
  /// </summary>
  IMusicService GetService(string provider);
}
