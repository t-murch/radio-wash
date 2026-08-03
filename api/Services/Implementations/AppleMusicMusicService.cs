using RadioWash.Api.Models.AppleMusic;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Adapter that exposes <see cref="IAppleMusicService"/> through the provider-agnostic
/// <see cref="IMusicService"/> contract, mirroring <see cref="SpotifyMusicService"/>.
/// Owns the Apple-specific concerns: library-vs-catalog id resolution (cleaners and copiers
/// always see catalog song ids), contentRating mapping, and clean-version search heuristics.
/// </summary>
public class AppleMusicMusicService : IMusicService
{
  public const string Provider = MusicProviders.AppleMusic;

  // Sized artwork URL: Apple returns a template with {w}x{h} placeholders.
  private const string ArtworkSize = "300";

  private readonly IAppleMusicService _appleMusic;
  private readonly ILogger<AppleMusicMusicService> _logger;

  public AppleMusicMusicService(IAppleMusicService appleMusic, ILogger<AppleMusicMusicService> logger)
  {
    _appleMusic = appleMusic;
    _logger = logger;
  }

  public string ProviderName => Provider;

  public async Task<MusicUserProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken)
  {
    // The Apple Music API deliberately exposes no user identity (no name/email endpoint).
    // Return a stable storefront-scoped placeholder; nothing in the pipeline renders it.
    var storefront = await _appleMusic.GetUserStorefrontAsync(userId, cancellationToken);
    return new MusicUserProfile($"apple_music:{storefront}", "Apple Music Library", null);
  }

  public async Task<IReadOnlyList<PlaylistSummary>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken)
  {
    var playlists = await _appleMusic.GetUserPlaylistsAsync(userId, cancellationToken);
    return playlists.Select(p => new PlaylistSummary(
      Id: p.Id,
      Name: p.Attributes.Name,
      Description: p.Attributes.Description?.Standard,
      ImageUrl: SizedArtworkUrl(p.Attributes.Artwork?.Url),
      // Library playlist attributes carry no track count; the pipeline recounts from the
      // real track list, so 0 only affects the pre-job display estimate.
      TrackCount: 0,
      OwnerId: string.Empty,
      OwnerName: null)).ToList();
  }

  public async Task<IReadOnlyList<MusicTrack>> GetPlaylistTracksAsync(
    int userId,
    string playlistId,
    CancellationToken cancellationToken)
  {
    var songs = (await _appleMusic.GetPlaylistTracksAsync(userId, playlistId, cancellationToken)).ToList();

    // Hydrate catalog attributes (ISRC for cross-catalog matching, authoritative
    // contentRating) in one batched pass over the distinct catalog ids.
    var catalogIds = songs
      .Select(s => s.CatalogId)
      .Where(id => !string.IsNullOrEmpty(id))
      .Select(id => id!)
      .Distinct()
      .ToList();
    var catalogById = (await _appleMusic.GetCatalogSongsByIdsAsync(userId, catalogIds, cancellationToken))
      .ToDictionary(c => c.Id, StringComparer.Ordinal);

    // Tracks without catalog linkage (personal uploads, region gaps) keep their library id;
    // they can't be searched, matched, or re-added cross-catalog and flow through the
    // pipeline as unmatchable rather than erroring.
    return songs.Select(s =>
    {
      var catalog = s.CatalogId is not null && catalogById.TryGetValue(s.CatalogId, out var c) ? c : null;
      return new MusicTrack(
        Id: s.CatalogId ?? s.Id,
        Name: s.Attributes.Name,
        IsExplicit: IsExplicitRating(s.Attributes.ContentRating ?? catalog?.Attributes.ContentRating),
        Artists: SplitArtists(s.Attributes.ArtistName),
        Isrc: catalog?.Attributes.Isrc,
        DurationMs: s.Attributes.DurationInMillis ?? catalog?.Attributes.DurationInMillis,
        AlbumName: s.Attributes.AlbumName ?? catalog?.Attributes.AlbumName);
    }).ToList();
  }

  public async Task<IReadOnlyDictionary<string, MusicTrack>> GetTracksByIsrcAsync(
    int userId,
    IReadOnlyCollection<string> isrcs,
    CancellationToken cancellationToken)
  {
    var songs = await _appleMusic.GetCatalogSongsByIsrcAsync(userId, isrcs, cancellationToken);
    // Multiple catalog songs can share an ISRC (album vs. single edition of the same
    // recording); the first is representative. Clean/explicit preference is applied by the
    // matcher, not here — clean editions are distinct recordings with their own ISRC.
    var byIsrc = new Dictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);
    foreach (var song in songs)
    {
      var isrc = song.Attributes.Isrc;
      if (!string.IsNullOrEmpty(isrc) && !byIsrc.ContainsKey(isrc))
      {
        byIsrc[isrc] = MapCatalogSong(song);
      }
    }
    return byIsrc;
  }

  public async Task<IReadOnlyList<MusicTrack>> SearchTracksAsync(
    int userId,
    string query,
    int limit,
    CancellationToken cancellationToken)
  {
    var songs = await _appleMusic.SearchCatalogSongsAsync(userId, query, limit, cancellationToken);
    return songs.Select(MapCatalogSong).ToList();
  }

  public async Task<MusicTrack?> FindCleanVersionAsync(
    int userId,
    MusicTrack explicitTrack,
    CancellationToken cancellationToken)
  {
    if (!explicitTrack.IsExplicit) return explicitTrack;

    var artists = string.Join(" ", explicitTrack.Artists.Select(a => a.Name));
    // Apple search has no explicit-exclusion operator (unlike Spotify's -tag:explicit);
    // fetch candidates and filter on contentRating.
    var candidates = await _appleMusic.SearchCatalogSongsAsync(
      userId, $"{explicitTrack.Name} {artists}", 10, cancellationToken);

    var clean = candidates.FirstOrDefault(c =>
      !IsExplicitRating(c.Attributes.ContentRating) &&
      TrackMatching.NamesMatch(explicitTrack.Name, c.Attributes.Name) &&
      TrackMatching.HasArtistOverlap(explicitTrack.Artists, c.Attributes.ArtistName) &&
      TrackMatching.DurationsCompatible(explicitTrack.DurationMs, c.Attributes.DurationInMillis));

    return clean is null ? null : MapCatalogSong(clean);
  }

  public async Task<PlaylistSummary> CreatePlaylistAsync(
    int userId,
    string name,
    string? description,
    CancellationToken cancellationToken)
  {
    var created = await _appleMusic.CreateLibraryPlaylistAsync(userId, name, description, cancellationToken);
    return new PlaylistSummary(
      created.Id,
      created.Attributes.Name,
      created.Attributes.Description?.Standard,
      SizedArtworkUrl(created.Attributes.Artwork?.Url),
      0,
      string.Empty,
      null);
  }

  public async Task AddTracksToPlaylistAsync(
    int userId,
    string playlistId,
    IEnumerable<string> trackIds,
    CancellationToken cancellationToken)
  {
    // IDs are catalog song ids; the client owns the {id, type: "songs"} body format the way
    // SpotifyMusicService owns spotify:track:<id> URIs.
    //
    // Library ids ("i.XXXX" — personal uploads, region-gap tracks) can still reach here: the
    // clean pipeline hands a non-explicit track's own id back as its "clean version", and for
    // a catalog-less track that id is the library id. Apple rejects a library id posted as a
    // catalog song, and one bad id fails its entire 25-track chunk — after the target
    // playlist was already created. Drop them instead: the track is absent from the result,
    // the same outcome the copy pipeline gives catalog-less tracks.
    var catalogIds = new List<string>();
    var skipped = new List<string>();
    foreach (var id in trackIds)
    {
      (IsCatalogSongId(id) ? catalogIds : skipped).Add(id);
    }

    if (skipped.Count > 0)
    {
      _logger.LogWarning(
        "Skipping {SkippedCount} non-catalog track id(s) while adding to Apple Music playlist {PlaylistId}: {SkippedIds}",
        skipped.Count, playlistId, string.Join(", ", skipped));
    }

    await _appleMusic.AddTracksToLibraryPlaylistAsync(userId, playlistId, catalogIds, cancellationToken);
  }

  internal static MusicTrack MapCatalogSong(AppleCatalogSong song) => new(
    Id: song.Id,
    Name: song.Attributes.Name,
    IsExplicit: IsExplicitRating(song.Attributes.ContentRating),
    Artists: SplitArtists(song.Attributes.ArtistName),
    Isrc: song.Attributes.Isrc,
    DurationMs: song.Attributes.DurationInMillis,
    AlbumName: song.Attributes.AlbumName);

  // Catalog song ids are purely numeric; library ids look like "i.XXXX" and are meaningless
  // outside the owner's library.
  private static bool IsCatalogSongId(string id) =>
    id.Length > 0 && id.All(char.IsAsciiDigit);

  private static bool IsExplicitRating(string? contentRating) =>
    string.Equals(contentRating, "explicit", StringComparison.OrdinalIgnoreCase);

  // Apple returns one joined artist string ("Artist A & Artist B"); keep it as a single
  // MusicArtist so name-overlap checks work against the full string.
  private static IReadOnlyList<MusicArtist> SplitArtists(string artistName) =>
    new List<MusicArtist> { new(artistName) };

  private static string? SizedArtworkUrl(string? templateUrl) =>
    templateUrl?
      .Replace("{w}", ArtworkSize, StringComparison.Ordinal)
      .Replace("{h}", ArtworkSize, StringComparison.Ordinal);
}
