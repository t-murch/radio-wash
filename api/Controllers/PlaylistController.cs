using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

[Route("api/[controller]")]
public class PlaylistController : AuthenticatedControllerBase
{
  private readonly ISpotifyService _spotifyService;

  public PlaylistController(
      ISpotifyService spotifyService,
      ILogger<PlaylistController> logger,
      RadioWashDbContext dbContext) : base(dbContext, logger)
  {
    _spotifyService = spotifyService;
  }

  /// <summary>
  /// Gets all playlists for the authenticated user
  /// </summary>
  [HttpGet("user/me")]
  public async Task<IActionResult> GetUserPlaylists()
  {
    try
    {
      var userId = GetCurrentUserId();
      Logger.LogInformation("Getting playlists for user {UserId}", userId);

      var playlists = await _spotifyService.GetUserPlaylistsAsync(userId);
      return Ok(playlists);
    }
    catch (UnauthorizedAccessException ex)
    {
      Logger.LogWarning(ex, "No Spotify connection for user {UserId}", GetCurrentUserId());
      return Ok(new
      {
        error = "spotify_not_connected",
        message = "Spotify account not connected. Please connect your Spotify account to view playlists.",
        playlists = new object[0]
      });
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error getting playlists");
      return StatusCode(500, new { error = "Failed to get playlists" });
    }
  }

  /// <summary>
  /// Gets all tracks in a playlist
  /// </summary>
  [HttpGet("playlist/{playlistId}/tracks")]
  public async Task<IActionResult> GetPlaylistTracks(string playlistId)
  {
    try
    {
      var userId = GetCurrentUserId();
      var tracks = await _spotifyService.GetPlaylistTracksAsync(userId, playlistId);

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
    catch (UnauthorizedAccessException ex)
    {
      Logger.LogWarning(ex, "No Spotify connection for user {UserId}", GetCurrentUserId());
      return BadRequest(new
      {
        error = "spotify_not_connected",
        message = "Spotify account not connected. Please connect your Spotify account to view playlist tracks."
      });
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error getting tracks for playlist {PlaylistId}", playlistId);
      return StatusCode(500, new { error = "Failed to get playlist tracks" });
    }
  }
}
