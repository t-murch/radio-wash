using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PlaylistController : ControllerBase
{
  private readonly ISpotifyService _spotifyService;
  private readonly ILogger<PlaylistController> _logger;

  public PlaylistController(
      ISpotifyService spotifyService,
      ILogger<PlaylistController> logger)
  {
    _spotifyService = spotifyService;
    _logger = logger;
  }

  /// <summary>
  /// Gets all playlists for the authenticated user
  /// </summary>
  [HttpGet("user/{userId}")]
  public async Task<IActionResult> GetUserPlaylists(int userId)
  {
    try
    {
      var playlists = await _spotifyService.GetUserPlaylistsAsync(userId);
      return Ok(playlists);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting playlists for user {UserId}", userId);
      return StatusCode(500, new { error = "Failed to get playlists" });
    }
  }

  /// <summary>
  /// Gets all tracks in a playlist
  /// </summary>
  [HttpGet("user/{userId}/playlist/{playlistId}/tracks")]
  public async Task<IActionResult> GetPlaylistTracks(int userId, string playlistId)
  {
    try
    {
      var tracks = await _spotifyService.GetPlaylistTracksAsync(userId, playlistId);

      // Map to simpler object for frontend
      var trackList = tracks.Select(t => new
      {
        id = t.Track.Id,
        name = t.Track.Name,
        artist = string.Join(", ", t.Track.Artists.Select(a => a.Name)),
        album = t.Track.Album.Name,
        albumCover = t.Track.Album.Images?.FirstOrDefault()?.Url,
        isExplicit = t.Track.Explicit,
        uri = t.Track.Uri
      }).ToList();

      return Ok(trackList);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting tracks for playlist {PlaylistId}", playlistId);
      return StatusCode(500, new { error = "Failed to get playlist tracks" });
    }
  }
}
