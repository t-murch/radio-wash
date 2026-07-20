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

  public AppleMusicMusicService(IAppleMusicService appleMusic)
  {
    _appleMusic = appleMusic;
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
    var songs = await _appleMusic.GetPlaylistTracksAsync(userId, playlistId, cancellationToken);
    // Tracks without catalog linkage (personal uploads, region gaps) keep their library id;
    // they can't be searched, matched, or re-added cross-catalog and flow through the
    // pipeline as unmatchable rather than erroring.
    return songs.Select(s => new MusicTrack(
      Id: s.CatalogId ?? s.Id,
      Name: s.Attributes.Name,
      IsExplicit: IsExplicitRating(s.Attributes.ContentRating),
      Artists: SplitArtists(s.Attributes.ArtistName))).ToList();
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
      NamesMatch(explicitTrack.Name, c.Attributes.Name) &&
      HasMatchingArtist(explicitTrack.Artists, c.Attributes.ArtistName));

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

  public Task AddTracksToPlaylistAsync(
    int userId,
    string playlistId,
    IEnumerable<string> trackIds,
    CancellationToken cancellationToken)
  {
    // IDs are catalog song ids; the client owns the {id, type: "songs"} body format the way
    // SpotifyMusicService owns spotify:track:<id> URIs.
    return _appleMusic.AddTracksToLibraryPlaylistAsync(userId, playlistId, trackIds, cancellationToken);
  }

  internal static MusicTrack MapCatalogSong(AppleCatalogSong song) => new(
    Id: song.Id,
    Name: song.Attributes.Name,
    IsExplicit: IsExplicitRating(song.Attributes.ContentRating),
    Artists: SplitArtists(song.Attributes.ArtistName));

  private static bool IsExplicitRating(string? contentRating) =>
    string.Equals(contentRating, "explicit", StringComparison.OrdinalIgnoreCase);

  // Apple returns one joined artist string ("Artist A & Artist B"); keep it as a single
  // MusicArtist so name-overlap checks work against the full string.
  private static IReadOnlyList<MusicArtist> SplitArtists(string artistName) =>
    new List<MusicArtist> { new(artistName) };

  private static bool NamesMatch(string sourceName, string candidateName)
  {
    return string.Equals(
      NormalizeName(sourceName), NormalizeName(candidateName), StringComparison.OrdinalIgnoreCase);
  }

  // Clean editions are frequently listed as "Song (Clean)" / "Song [Clean]".
  private static string NormalizeName(string name)
  {
    var normalized = name.Trim();
    foreach (var suffix in new[] { "(clean)", "[clean]" })
    {
      if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
      {
        normalized = normalized[..^suffix.Length].TrimEnd();
      }
    }
    return normalized;
  }

  private static bool HasMatchingArtist(IReadOnlyList<MusicArtist> sourceArtists, string candidateArtistName)
  {
    // The candidate side is a single joined string; match when any source artist appears in
    // it (or vice versa for single-artist sources).
    return sourceArtists.Any(a =>
      candidateArtistName.Contains(a.Name, StringComparison.OrdinalIgnoreCase) ||
      a.Name.Contains(candidateArtistName, StringComparison.OrdinalIgnoreCase));
  }

  private static string? SizedArtworkUrl(string? templateUrl) =>
    templateUrl?
      .Replace("{w}", ArtworkSize, StringComparison.Ordinal)
      .Replace("{h}", ArtworkSize, StringComparison.Ordinal);
}
