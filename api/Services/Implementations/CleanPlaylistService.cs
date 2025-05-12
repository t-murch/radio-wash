using Hangfire;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class CleanPlaylistService : ICleanPlaylistService
{
  private readonly RadioWashDbContext _dbContext;
  private readonly ISpotifyService _spotifyService;
  private readonly ILogger<CleanPlaylistService> _logger;
  private readonly IServiceScopeFactory _serviceScopeFactory;

  public CleanPlaylistService(
      RadioWashDbContext dbContext,
      ISpotifyService spotifyService,
      ILogger<CleanPlaylistService> logger,
      IServiceScopeFactory serviceScopeFactory)
  {
    _dbContext = dbContext;
    _spotifyService = spotifyService;
    _logger = logger;
    _serviceScopeFactory = serviceScopeFactory;
  }

  public async Task<CleanPlaylistJobDto> CreateJobAsync(int userId, CreateCleanPlaylistJobDto request)
  {
    // Fetch the playlist details
    var userPlaylists = await _spotifyService.GetUserPlaylistsAsync(userId);
    var sourcePlaylist = userPlaylists.FirstOrDefault(p => p.Id == request.SourcePlaylistId);

    if (sourcePlaylist == null)
    {
      throw new Exception("Playlist not found or you don't have access to it");
    }

    // Create the job
    var job = new CleanPlaylistJob
    {
      UserId = userId,
      SourcePlaylistId = sourcePlaylist.Id,
      SourcePlaylistName = sourcePlaylist.Name,
      TargetPlaylistName = request.TargetPlaylistName ?? $"Clean - {sourcePlaylist.Name}",
      Status = JobStatus.Created,
      TotalTracks = sourcePlaylist.TrackCount,
      ProcessedTracks = 0,
      MatchedTracks = 0
    };

    _dbContext.CleanPlaylistJobs.Add(job);
    await _dbContext.SaveChangesAsync();

    // Start processing the job asynchronously
    // Note: In a production environment, this would be handled by a background job processor
    // _ = Task.Run(() => ProcessJobAsync(job.Id));
    BackgroundJob.Enqueue(() => ProcessJobAsync(job.Id));

    return MapToDto(job);
  }

  public async Task<CleanPlaylistJobDto> GetJobAsync(int userId, int jobId)
  {
    var job = await _dbContext.CleanPlaylistJobs
        .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId);

    if (job == null)
    {
      throw new Exception("Job not found");
    }

    return MapToDto(job);
  }

  public async Task<List<CleanPlaylistJobDto>> GetUserJobsAsync(int userId)
  {
    var jobs = await _dbContext.CleanPlaylistJobs
        .Where(j => j.UserId == userId)
        .OrderByDescending(j => j.CreatedAt)
        .ToListAsync();

    return jobs.Select(MapToDto).ToList();
  }

  public async Task ProcessJobInternalAsync(int jobId, RadioWashDbContext dbContext, ISpotifyService spotifyService, ILogger<CleanPlaylistService> logger)
  {
    var job = await _dbContext.CleanPlaylistJobs
                .Include(j => j.TrackMappings)
                .FirstOrDefaultAsync(j => j.Id == jobId);

    if (job == null)
    {
      _logger.LogError("Job {JobId} not found", jobId);
      return;
    }

    try
    {
      // Update job status to Processing
      job.Status = JobStatus.Processing;
      job.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();

      // Get all tracks from the source playlist
      var tracks = await _spotifyService.GetPlaylistTracksAsync(job.UserId, job.SourcePlaylistId);
      job.TotalTracks = tracks.Count;

      // Create a new playlist for the clean tracks
      job.TargetPlaylistId = await _spotifyService.CreatePlaylistAsync(
          job.UserId,
          job.TargetPlaylistName,
          $"Clean version of {job.SourcePlaylistName} created by RadioWash"
      );

      // Process each track and find clean alternatives
      var cleanTrackUris = new List<string>();

      foreach (var playlistTrack in tracks)
      {
        var track = playlistTrack.Track;
        job.ProcessedTracks++;

        var trackMapping = new TrackMapping
        {
          JobId = job.Id,
          SourceTrackId = track.Id,
          SourceTrackName = track.Name,
          SourceArtistName = string.Join(", ", track.Artists.Select(a => a.Name)),
          IsExplicit = track.Explicit,
          HasCleanMatch = false
        };

        // If the track is not explicit, add it directly
        if (!track.Explicit)
        {
          trackMapping.TargetTrackId = track.Id;
          trackMapping.TargetTrackName = track.Name;
          trackMapping.TargetArtistName = trackMapping.SourceArtistName;
          trackMapping.HasCleanMatch = true;
          job.MatchedTracks++;

          cleanTrackUris.Add(track.Uri);
        }
        else
        {
          // Find a clean alternative
          var cleanTrack = await FindCleanAlternativeAsync(job.UserId, track);

          if (cleanTrack != null)
          {
            trackMapping.TargetTrackId = cleanTrack.Id;
            trackMapping.TargetTrackName = cleanTrack.Name;
            trackMapping.TargetArtistName = string.Join(", ", cleanTrack.Artists.Select(a => a.Name));
            trackMapping.HasCleanMatch = true;
            job.MatchedTracks++;

            cleanTrackUris.Add(cleanTrack.Uri);
          }
        }

        _dbContext.TrackMappings.Add(trackMapping);

        // Periodically save progress
        if (job.ProcessedTracks % 10 == 0 || job.ProcessedTracks == job.TotalTracks)
        {
          job.UpdatedAt = DateTime.UtcNow;
          await _dbContext.SaveChangesAsync();
        }
      }

      // Add all clean tracks to the new playlist
      if (cleanTrackUris.Any())
      {
        await _spotifyService.AddTracksToPlaylistAsync(job.UserId, job.TargetPlaylistId, cleanTrackUris);
      }

      // Update job status to Completed
      job.Status = JobStatus.Completed;
      job.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing job {JobId}", jobId);

      // Update job status to Failed
      job.Status = JobStatus.Failed;
      job.ErrorMessage = ex.Message;
      job.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();
    }
  }

  [AutomaticRetry(Attempts = 3)]
  public async Task ProcessJobAsync(int jobId)
  {
    var job = await _dbContext.CleanPlaylistJobs
        .Include(j => j.TrackMappings)
        .FirstOrDefaultAsync(j => j.Id == jobId);

    if (job == null)
    {
      _logger.LogError("Job {JobId} not found", jobId);
      return;
    }

    try
    {
      // Update job status to Processing
      job.Status = JobStatus.Processing;
      job.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();

      // Get all tracks from the source playlist
      var tracks = await _spotifyService.GetPlaylistTracksAsync(job.UserId, job.SourcePlaylistId);
      job.TotalTracks = tracks.Count;

      // Create a new playlist for the clean tracks
      job.TargetPlaylistId = await _spotifyService.CreatePlaylistAsync(
          job.UserId,
          job.TargetPlaylistName,
          $"Clean version of {job.SourcePlaylistName} created by RadioWash"
      );

      // Process each track and find clean alternatives
      var cleanTrackUris = new List<string>();

      foreach (var playlistTrack in tracks)
      {
        var track = playlistTrack.Track;
        job.ProcessedTracks++;

        var trackMapping = new TrackMapping
        {
          JobId = job.Id,
          SourceTrackId = track.Id,
          SourceTrackName = track.Name,
          SourceArtistName = string.Join(", ", track.Artists.Select(a => a.Name)),
          IsExplicit = track.Explicit,
          HasCleanMatch = false
        };

        // If the track is not explicit, add it directly
        if (!track.Explicit)
        {
          trackMapping.TargetTrackId = track.Id;
          trackMapping.TargetTrackName = track.Name;
          trackMapping.TargetArtistName = trackMapping.SourceArtistName;
          trackMapping.HasCleanMatch = true;
          job.MatchedTracks++;

          cleanTrackUris.Add(track.Uri);
        }
        else
        {
          // Find a clean alternative
          var cleanTrack = await FindCleanAlternativeAsync(job.UserId, track);

          if (cleanTrack != null)
          {
            trackMapping.TargetTrackId = cleanTrack.Id;
            trackMapping.TargetTrackName = cleanTrack.Name;
            trackMapping.TargetArtistName = string.Join(", ", cleanTrack.Artists.Select(a => a.Name));
            trackMapping.HasCleanMatch = true;
            job.MatchedTracks++;

            cleanTrackUris.Add(cleanTrack.Uri);
          }
        }

        _dbContext.TrackMappings.Add(trackMapping);

        // Periodically save progress
        if (job.ProcessedTracks % 10 == 0 || job.ProcessedTracks == job.TotalTracks)
        {
          job.UpdatedAt = DateTime.UtcNow;
          await _dbContext.SaveChangesAsync();
        }
      }

      // Add all clean tracks to the new playlist
      if (cleanTrackUris.Any())
      {
        await _spotifyService.AddTracksToPlaylistAsync(job.UserId, job.TargetPlaylistId, cleanTrackUris);
      }

      // Update job status to Completed
      job.Status = JobStatus.Completed;
      job.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing job {JobId}", jobId);

      // Update job status to Failed
      job.Status = JobStatus.Failed;
      job.ErrorMessage = ex.Message;
      job.UpdatedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();
    }
  }

  public async Task<List<TrackMappingDto>> GetJobTrackMappingsAsync(int userId, int jobId)
  {
    var job = await _dbContext.CleanPlaylistJobs
        .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId);

    if (job == null)
    {
      throw new Exception("Job not found");
    }

    var trackMappings = await _dbContext.TrackMappings
        .Where(t => t.JobId == jobId)
        .OrderBy(t => t.Id)
        .ToListAsync();

    return trackMappings.Select(MapToDto).ToList();
  }

  private async Task<SpotifyTrack?> FindCleanAlternativeAsync(int userId, SpotifyTrack explicitTrack)
  {
    try
    {
      // Build a search query: "[track name]" artist:[artist name]
      var artistName = explicitTrack.Artists.First().Name;
      var query = $"\"{explicitTrack.Name}\" artist:{artistName}";

      // Search for alternative tracks
      var searchResults = await _spotifyService.SearchTracksAsync(userId, query, 10);

      // Filter out explicit tracks and the original track
      var potentialMatches = searchResults
          .Where(t => !t.Explicit && t.Id != explicitTrack.Id)
          .ToList();

      if (potentialMatches.Any())
      {
        // Sort by popularity and name similarity
        var tracksByRelevance = potentialMatches
            .OrderByDescending(t => t.Popularity)
            .ThenBy(t => LevenshteinDistance(t.Name.ToLower(), explicitTrack.Name.ToLower()))
            .ToList();

        return tracksByRelevance.First();
      }

      // If no exact match found, try a broader search
      query = $"{explicitTrack.Name} {artistName}";
      searchResults = await _spotifyService.SearchTracksAsync(userId, query, 20);

      potentialMatches = searchResults
          .Where(t => !t.Explicit && t.Id != explicitTrack.Id)
          .ToList();

      if (potentialMatches.Any())
      {
        // Calculate a relevance score based on name similarity, artist match, and popularity
        var scoredMatches = potentialMatches.Select(t =>
        {
          var nameSimilarity = 1 - (LevenshteinDistance(t.Name.ToLower(), explicitTrack.Name.ToLower()) / (double)Math.Max(t.Name.Length, explicitTrack.Name.Length));
          var artistMatch = t.Artists.Any(a => explicitTrack.Artists.Any(ea => ea.Id == a.Id)) ? 1.0 : 0.5;
          var popularityScore = t.Popularity / 100.0;

          return new
          {
            Track = t,
            Score = (nameSimilarity * 0.5) + (artistMatch * 0.3) + (popularityScore * 0.2)
          };
        }).OrderByDescending(t => t.Score);

        var bestMatch = scoredMatches.FirstOrDefault();
        if (bestMatch != null && bestMatch.Score > 0.7)
        {
          return bestMatch.Track;
        }
      }

      return null;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error finding clean alternative for track {TrackId}", explicitTrack.Id);
      return null;
    }
  }

  // Helper method to calculate string edit distance
  private static int LevenshteinDistance(string s, string t)
  {
    var n = s.Length;
    var m = t.Length;
    var d = new int[n + 1, m + 1];

    if (n == 0) return m;
    if (m == 0) return n;

    for (var i = 0; i <= n; i++) d[i, 0] = i;
    for (var j = 0; j <= m; j++) d[0, j] = j;

    for (var i = 1; i <= n; i++)
    {
      for (var j = 1; j <= m; j++)
      {
        var cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
        d[i, j] = Math.Min(
            Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
            d[i - 1, j - 1] + cost);
      }
    }

    return d[n, m];
  }

  private static CleanPlaylistJobDto MapToDto(CleanPlaylistJob job)
  {
    return new CleanPlaylistJobDto
    {
      Id = job.Id,
      SourcePlaylistId = job.SourcePlaylistId,
      SourcePlaylistName = job.SourcePlaylistName,
      TargetPlaylistId = job.TargetPlaylistId,
      TargetPlaylistName = job.TargetPlaylistName,
      Status = job.Status.ToString(),
      ErrorMessage = job.ErrorMessage,
      TotalTracks = job.TotalTracks,
      ProcessedTracks = job.ProcessedTracks,
      MatchedTracks = job.MatchedTracks,
      CreatedAt = job.CreatedAt,
      UpdatedAt = job.UpdatedAt
    };
  }

  private static TrackMappingDto MapToDto(TrackMapping mapping)
  {
    return new TrackMappingDto
    {
      Id = mapping.Id,
      SourceTrackId = mapping.SourceTrackId,
      SourceTrackName = mapping.SourceTrackName,
      SourceArtistName = mapping.SourceArtistName,
      IsExplicit = mapping.IsExplicit,
      TargetTrackId = mapping.TargetTrackId,
      TargetTrackName = mapping.TargetTrackName,
      TargetArtistName = mapping.TargetArtistName,
      HasCleanMatch = mapping.HasCleanMatch
    };
  }
}
