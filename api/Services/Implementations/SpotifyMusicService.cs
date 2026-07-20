using RadioWash.Api.Models.Music;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Adapter that exposes the existing <see cref="ISpotifyService"/> HTTP client through the
/// provider-agnostic <see cref="IMusicService"/> contract. Owns the Spotify-specific URI
/// format (<c>spotify:track:&lt;id&gt;</c>) and the Spotify-shaped DTO-to-record mapping so
/// downstream cleaners stay platform-neutral.
/// </summary>
public class SpotifyMusicService : IMusicService
{
  public const string Provider = "spotify";

  private readonly ISpotifyService _spotify;

  public SpotifyMusicService(ISpotifyService spotify)
  {
    _spotify = spotify;
  }

  public string ProviderName => Provider;

  public async Task<MusicUserProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken)
  {
    var profile = await _spotify.GetUserProfileAsync(userId, cancellationToken);
    return new MusicUserProfile(profile.Id, profile.DisplayName, profile.Email);
  }

  public async Task<IReadOnlyList<PlaylistSummary>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken)
  {
    var playlists = await _spotify.GetUserPlaylistsAsync(userId, cancellationToken);
    return playlists.Select(p => new PlaylistSummary(
      p.Id, p.Name, p.Description, p.ImageUrl, p.TrackCount, p.OwnerId, p.OwnerName)).ToList();
  }

  public async Task<IReadOnlyList<MusicTrack>> GetPlaylistTracksAsync(
    int userId,
    string playlistId,
    CancellationToken cancellationToken)
  {
    var tracks = await _spotify.GetPlaylistTracksAsync(userId, playlistId, cancellationToken);
    return tracks.Select(MapTrack).ToList();
  }

  public async Task<MusicTrack?> FindCleanVersionAsync(
    int userId,
    MusicTrack explicitTrack,
    CancellationToken cancellationToken)
  {
    var spotifyInput = ToSpotifyTrack(explicitTrack);
    var cleanVersion = await _spotify.FindCleanVersionAsync(userId, spotifyInput, cancellationToken);
    return cleanVersion is null ? null : MapTrack(cleanVersion);
  }

  public async Task<IReadOnlyDictionary<string, MusicTrack>> GetTracksByIsrcAsync(
    int userId,
    IReadOnlyCollection<string> isrcs,
    CancellationToken cancellationToken)
  {
    // Spotify has no batch ISRC endpoint — one search per ISRC. The client's 429 handling
    // paces large lookups; the copier caps per-job volume.
    var results = new Dictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);
    foreach (var isrc in isrcs)
    {
      var track = await _spotify.GetTrackByIsrcAsync(userId, isrc, cancellationToken);
      if (track is not null)
      {
        results[isrc] = MapTrack(track);
      }
    }
    return results;
  }

  public async Task<IReadOnlyList<MusicTrack>> SearchTracksAsync(
    int userId,
    string query,
    int limit,
    CancellationToken cancellationToken)
  {
    var tracks = await _spotify.SearchTracksAsync(userId, query, limit, cancellationToken);
    return tracks.Select(MapTrack).ToList();
  }

  public async Task<PlaylistSummary> CreatePlaylistAsync(
    int userId,
    string name,
    string? description,
    CancellationToken cancellationToken)
  {
    var playlist = await _spotify.CreatePlaylistAsync(userId, name, description, cancellationToken);
    return new PlaylistSummary(
      playlist.Id,
      playlist.Name,
      playlist.Description,
      playlist.Images?.FirstOrDefault()?.Url,
      playlist.Tracks?.Total ?? 0,
      playlist.Owner?.Id ?? string.Empty,
      playlist.Owner?.DisplayName);
  }

  public Task AddTracksToPlaylistAsync(
    int userId,
    string playlistId,
    IEnumerable<string> trackIds,
    CancellationToken cancellationToken)
  {
    // Spotify expects URIs shaped spotify:track:<id>. Callers pass raw platform IDs so the
    // cleaner stays provider-neutral.
    var uris = trackIds.Select(id => $"spotify:track:{id}");
    return _spotify.AddTracksToPlaylistAsync(userId, playlistId, uris, cancellationToken);
  }

  private static MusicTrack MapTrack(SpotifyTrack t) => new(
    Id: t.Id,
    Name: t.Name,
    IsExplicit: t.Explicit,
    Artists: (t.Artists ?? Array.Empty<SpotifyArtist>())
      .Select(a => new MusicArtist(a.Name))
      .ToList(),
    Isrc: t.ExternalIds?.Isrc,
    DurationMs: t.DurationMs,
    AlbumName: t.Album?.Name);

  private static SpotifyTrack ToSpotifyTrack(MusicTrack t) => new()
  {
    Id = t.Id,
    Name = t.Name,
    Explicit = t.IsExplicit,
    Artists = t.Artists.Select(a => new SpotifyArtist { Id = string.Empty, Name = a.Name }).ToArray(),
    Album = new SpotifyAlbum { Id = string.Empty, Name = string.Empty },
    Uri = $"spotify:track:{t.Id}"
  };
}
