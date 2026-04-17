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
    var profile = await _spotify.GetUserProfileAsync(userId);
    return new MusicUserProfile(profile.Id, profile.DisplayName, profile.Email);
  }

  public async Task<IReadOnlyList<PlaylistSummary>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken)
  {
    var playlists = await _spotify.GetUserPlaylistsAsync(userId);
    return playlists.Select(p => new PlaylistSummary(
      p.Id, p.Name, p.Description, p.ImageUrl, p.TrackCount, p.OwnerId, p.OwnerName)).ToList();
  }

  public async Task<IReadOnlyList<MusicTrack>> GetPlaylistTracksAsync(
    int userId,
    string playlistId,
    CancellationToken cancellationToken)
  {
    var tracks = await _spotify.GetPlaylistTracksAsync(userId, playlistId);
    return tracks.Select(MapTrack).ToList();
  }

  public async Task<MusicTrack?> FindCleanVersionAsync(
    int userId,
    MusicTrack explicitTrack,
    CancellationToken cancellationToken)
  {
    var spotifyInput = ToSpotifyTrack(explicitTrack);
    var cleanVersion = await _spotify.FindCleanVersionAsync(userId, spotifyInput);
    return cleanVersion is null ? null : MapTrack(cleanVersion);
  }

  public async Task<PlaylistSummary> CreatePlaylistAsync(
    int userId,
    string name,
    string? description,
    CancellationToken cancellationToken)
  {
    var playlist = await _spotify.CreatePlaylistAsync(userId, name, description);
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
    return _spotify.AddTracksToPlaylistAsync(userId, playlistId, uris);
  }

  private static MusicTrack MapTrack(SpotifyTrack t) => new(
    Id: t.Id,
    Name: t.Name,
    IsExplicit: t.Explicit,
    Artists: (t.Artists ?? Array.Empty<SpotifyArtist>())
      .Select(a => new MusicArtist(a.Name))
      .ToList());

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
