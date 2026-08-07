using Microsoft.AspNetCore.Mvc;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

[Route("api/[controller]")]
public class PlaylistController : AuthenticatedControllerBase
{
  private readonly IMusicServiceFactory _musicServiceFactory;

  public PlaylistController(
      IMusicServiceFactory musicServiceFactory,
      ILogger<PlaylistController> logger,
      RadioWashDbContext dbContext) : base(dbContext, logger)
  {
    _musicServiceFactory = musicServiceFactory;
  }

  /// <summary>
  /// Gets all playlists for the authenticated user on the given provider (default Apple Music)
  /// </summary>
  [HttpGet("user/me")]
  public async Task<IActionResult> GetUserPlaylists([FromQuery] string? provider = null)
  {
    if (!TryResolveProvider(provider, out var normalizedProvider, out var badRequest))
    {
      return badRequest!;
    }

    try
    {
      var userId = GetCurrentUserId();
      Logger.LogInformation("Getting {Provider} playlists for user {UserId}", normalizedProvider, userId);

      var playlists = await _musicServiceFactory.GetService(normalizedProvider)
          .GetUserPlaylistsAsync(userId, HttpContext.RequestAborted);
      return Ok(playlists);
    }
    catch (UnauthorizedAccessException ex)
    {
      Logger.LogWarning(ex, "No {Provider} connection for user {UserId}", normalizedProvider, GetCurrentUserId());
      return Ok(new
      {
        error = $"{normalizedProvider}_not_connected",
        message = $"{ProviderLabel(normalizedProvider)} account not connected. Please connect your {ProviderLabel(normalizedProvider)} account to view playlists.",
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
  /// Gets all tracks in a playlist on the given provider (default Apple Music)
  /// </summary>
  [HttpGet("playlist/{playlistId}/tracks")]
  public async Task<IActionResult> GetPlaylistTracks(string playlistId, [FromQuery] string? provider = null)
  {
    if (!TryResolveProvider(provider, out var normalizedProvider, out var badRequest))
    {
      return badRequest!;
    }

    try
    {
      var userId = GetCurrentUserId();
      var tracks = await _musicServiceFactory.GetService(normalizedProvider)
          .GetPlaylistTracksAsync(userId, playlistId, HttpContext.RequestAborted);

      // Map to simpler object for frontend
      var trackList = tracks.Select(t => new
      {
        id = t.Id,
        name = t.Name,
        artist = string.Join(", ", t.Artists.Select(a => a.Name)),
        isExplicit = t.IsExplicit
      }).ToList();

      return Ok(trackList);
    }
    catch (UnauthorizedAccessException ex)
    {
      Logger.LogWarning(ex, "No {Provider} connection for user {UserId}", normalizedProvider, GetCurrentUserId());
      return BadRequest(new
      {
        error = $"{normalizedProvider}_not_connected",
        message = $"{ProviderLabel(normalizedProvider)} account not connected. Please connect your {ProviderLabel(normalizedProvider)} account to view playlist tracks."
      });
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error getting tracks for playlist {PlaylistId}", playlistId);
      return StatusCode(500, new { error = "Failed to get playlist tracks" });
    }
  }

  private bool TryResolveProvider(string? provider, out string normalizedProvider, out IActionResult? badRequest)
  {
    badRequest = null;
    if (string.IsNullOrWhiteSpace(provider))
    {
      normalizedProvider = MusicProviders.AppleMusic;
      return true;
    }

    if (MusicProviders.TryNormalize(provider, out normalizedProvider))
    {
      return true;
    }

    badRequest = BadRequest(new { error = "unsupported_provider", message = $"Provider '{provider}' is not supported." });
    return false;
  }

  private static string ProviderLabel(string provider) => provider switch
  {
    MusicProviders.AppleMusic => "Apple Music",
    _ => provider
  };
}
