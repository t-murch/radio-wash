using RadioWash.Api.Models.AppleMusic;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// HTTP client for the Apple Music API. Every user-scoped call authenticates with the
/// developer token (app identity) plus the user's Music-User-Token. Catalog calls are
/// storefront-scoped; the user's storefront is resolved and cached per user.
/// </summary>
public interface IAppleMusicService
{
  Task<string> GetUserStorefrontAsync(int userId, CancellationToken cancellationToken = default);

  Task<IEnumerable<AppleLibraryPlaylist>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken = default);

  Task<IEnumerable<AppleLibrarySong>> GetPlaylistTracksAsync(int userId, string playlistId, CancellationToken cancellationToken = default);

  /// <summary>Fetches catalog songs by ISRC. A single ISRC can map to multiple songs.</summary>
  Task<IReadOnlyList<AppleCatalogSong>> GetCatalogSongsByIsrcAsync(int userId, IReadOnlyCollection<string> isrcs, CancellationToken cancellationToken = default);

  /// <summary>Fetches catalog songs by catalog id (e.g. to hydrate ISRC for library tracks).</summary>
  Task<IReadOnlyList<AppleCatalogSong>> GetCatalogSongsByIdsAsync(int userId, IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);

  Task<IReadOnlyList<AppleCatalogSong>> SearchCatalogSongsAsync(int userId, string term, int limit, CancellationToken cancellationToken = default);

  Task<AppleLibraryPlaylist> CreateLibraryPlaylistAsync(int userId, string name, string? description, CancellationToken cancellationToken = default);

  /// <summary>Adds catalog songs (by catalog id) to a library playlist.</summary>
  Task AddTracksToLibraryPlaylistAsync(int userId, string playlistId, IEnumerable<string> catalogSongIds, CancellationToken cancellationToken = default);
}
