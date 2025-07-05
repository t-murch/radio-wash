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
      // log this userId
      _logger.LogDebug("Getting playlists for user {UserId}", userId);
      var playlists = await _spotifyService.GetUserPlaylistsAsync(userId.ToString());
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
      var tracks = await _spotifyService.GetPlaylistTracksAsync(userId.ToString(), playlistId);

      // Map to simpler object for frontend
      var trackList = tracks.Select(t => new
      {
        id = t.Id,
        name = t.Name,
        artist = string.Join(", ", t.Artists.Select(a => a.Name)),
        album = t.Album.Name,
        albumCover = t.Album.Images?.FirstOrDefault()?.Url,
        isExplicit = t.Explicit,
        uri = t.Uri
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
