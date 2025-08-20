using Moq;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace RadioWash.Api.Test.Integration;

public class CleanPlaylistServiceIntegrationTests : IntegrationTestBase
{
  private readonly Mock<ISpotifyService> _spotifyServiceMock;
  private readonly Mock<IBackgroundJobClient> _backgroundJobClientMock;
  private readonly Mock<IProgressBroadcastService> _progressBroadcastServiceMock;
  private readonly CleanPlaylistService _sut; // System Under Test
  private readonly IUserRepository _userRepository;
  private readonly ICleanPlaylistJobRepository _jobRepository;
  private readonly ITrackMappingRepository _trackMappingRepository;

  public CleanPlaylistServiceIntegrationTests()
  {
    // Setup mocks
    _spotifyServiceMock = new Mock<ISpotifyService>();
    _backgroundJobClientMock = new Mock<IBackgroundJobClient>();
    _progressBroadcastServiceMock = new Mock<IProgressBroadcastService>();
    var loggerMock = new Mock<ILogger<CleanPlaylistService>>();

    // Get repositories from DI container
    _userRepository = Scope.ServiceProvider.GetRequiredService<IUserRepository>();
    _jobRepository = Scope.ServiceProvider.GetRequiredService<ICleanPlaylistJobRepository>();
    _trackMappingRepository = Scope.ServiceProvider.GetRequiredService<ITrackMappingRepository>();

    // Instantiate the service with real database context and repositories
    _sut = new CleanPlaylistService(
        DbContext!,
        _userRepository,
        _jobRepository,
        _trackMappingRepository,
        _spotifyServiceMock.Object,
        loggerMock.Object,
        _backgroundJobClientMock.Object,
        _progressBroadcastServiceMock.Object
    );
  }

  [Fact]
  public async Task CreateJobAsync_WhenUserAndPlaylistExist_ShouldCreateAndEnqueueJob()
  {
    // Arrange
    var userId = 1;
    var supabaseId = "user-supabase-id-123";
    var playlistId = "spotify-playlist-id-456";

    // 1. Seed the database with a user
    var user = new User { Id = userId, SupabaseId = supabaseId, DisplayName = "Test User", Email = "test@test.com" };
    await DbContext!.Users.AddAsync(user);
    await DbContext!.SaveChangesAsync();

    // 2. Mock the SpotifyService response
    var mockPlaylistDto = new PlaylistDto { Id = playlistId, Name = "My Awesome Mix", TrackCount = 50 };
    _spotifyServiceMock
        .Setup(s => s.GetUserPlaylistsAsync(1))
        .ReturnsAsync(new List<PlaylistDto> { mockPlaylistDto });

    var createJobDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = playlistId,
      TargetPlaylistName = "My Clean Mix"
    };

    // Act
    var result = await _sut.CreateJobAsync(userId, createJobDto);

    // Assert
    // 1. Verify the result DTO is correct
    Assert.NotNull(result);
    Assert.Equal("My Clean Mix", result.TargetPlaylistName);
    Assert.Equal(JobStatus.Pending, result.Status);
    Assert.Equal(50, result.TotalTracks);

    // 2. Verify a job was actually saved to the database
    var jobInDb = await DbContext!.CleanPlaylistJobs.FirstOrDefaultAsync();
    Assert.NotNull(jobInDb);
    Assert.Equal(userId, jobInDb.UserId);
    Assert.Equal(playlistId, jobInDb.SourcePlaylistId);

    // 3. Verify the background job was enqueued (using the base Create method since Enqueue is an extension)
    _backgroundJobClientMock.Verify(
        client => client.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()),
        Times.Once
    );
  }

  [Fact]
  public async Task CreateJobAsync_WhenUserDoesNotExist_ShouldThrowKeyNotFoundException()
  {
    // Arrange
    var nonExistentUserId = 999;
    var createJobDto = new CreateCleanPlaylistJobDto { SourcePlaylistId = "any-playlist" };

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.CreateJobAsync(nonExistentUserId, createJobDto));
  }

  [Fact]
  public async Task CreateJobAsync_WhenPlaylistNotFound_ShouldThrowKeyNotFoundException()
  {
    // Arrange
    var userId = 1;
    var user = new User { Id = userId, SupabaseId = "user-123", DisplayName = "Test User", Email = "test@test.com" };
    await DbContext!.Users.AddAsync(user);
    await DbContext!.SaveChangesAsync();

    // Mock empty playlist list (playlist not found)
    _spotifyServiceMock
        .Setup(s => s.GetUserPlaylistsAsync(userId))
        .ReturnsAsync(new List<PlaylistDto>());

    var createJobDto = new CreateCleanPlaylistJobDto { SourcePlaylistId = "non-existent-playlist" };

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.CreateJobAsync(userId, createJobDto));
  }

  [Fact]
  public async Task CreateJobAsync_WithEmptyTargetName_ShouldUseDefaultNaming()
  {
    // Arrange
    var userId = 1;
    var user = new User { Id = userId, SupabaseId = "user-123", DisplayName = "Test User", Email = "test@test.com" };
    await DbContext!.Users.AddAsync(user);
    await DbContext!.SaveChangesAsync();

    var mockPlaylistDto = new PlaylistDto { Id = "playlist-123", Name = "My Awesome Mix", TrackCount = 25 };
    _spotifyServiceMock
        .Setup(s => s.GetUserPlaylistsAsync(userId))
        .ReturnsAsync(new List<PlaylistDto> { mockPlaylistDto });

    var createJobDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "playlist-123",
      TargetPlaylistName = "" // Empty string should trigger default naming
    };

    // Act
    var result = await _sut.CreateJobAsync(userId, createJobDto);

    // Assert
    Assert.Equal("Clean - My Awesome Mix", result.TargetPlaylistName);

    var jobInDb = await DbContext!.CleanPlaylistJobs.FirstOrDefaultAsync();
    Assert.NotNull(jobInDb);
    Assert.Equal("Clean - My Awesome Mix", jobInDb.TargetPlaylistName);
  }

  [Fact]
  public async Task ProcessJobAsync_WhenJobNotFound_ShouldLogErrorAndReturn()
  {
    // Arrange
    var nonExistentJobId = 999;
    var loggerMock = new Mock<ILogger<CleanPlaylistService>>();
    var service = new CleanPlaylistService(
        DbContext!,
        _userRepository,
        _jobRepository,
        _trackMappingRepository,
        _spotifyServiceMock.Object,
        loggerMock.Object,
        _backgroundJobClientMock.Object,
        _progressBroadcastServiceMock.Object);

    // Act
    await service.ProcessJobAsync(nonExistentJobId);

    // Assert
    loggerMock.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Job 999 not found for processing")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
  }

  [Fact]
  public async Task ProcessJobAsync_WhenUserNotFound_ShouldFailJob()
  {
    // Arrange - Disable FK constraints temporarily
    await DbContext!.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
    
    var job = new CleanPlaylistJob
    {
      Id = 1,
      UserId = 999, // Non-existent user
      SourcePlaylistId = "playlist-123",
      SourcePlaylistName = "Test Playlist", 
      TargetPlaylistName = "Clean Test",
      Status = JobStatus.Pending,
      TotalTracks = 10,
      ProcessedTracks = 0,
      MatchedTracks = 0,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    
    await DbContext!.CleanPlaylistJobs.AddAsync(job);
    await DbContext!.SaveChangesAsync();
    
    // Re-enable FK constraints
    await DbContext!.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");

    // Act
    await _sut.ProcessJobAsync(job.Id);

    // Assert
    var updatedJob = await DbContext!.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(updatedJob);
    Assert.Equal(JobStatus.Failed, updatedJob.Status);
    Assert.Contains("User for job 1 not found", updatedJob.ErrorMessage!);
  }

  [Fact]
  public async Task ProcessJobAsync_SuccessfulProcessing_ShouldCompleteJob()
  {
    // Arrange
    var userId = 1;
    var user = new User { Id = userId, SupabaseId = "user-123", DisplayName = "Test User", Email = "test@test.com" };
    await DbContext!.Users.AddAsync(user);

    var job = new CleanPlaylistJob
    {
      Id = 1,
      UserId = userId,
      SourcePlaylistId = "playlist-123",
      SourcePlaylistName = "Test Playlist",
      TargetPlaylistName = "Clean Test",
      Status = JobStatus.Pending,
      TotalTracks = 2
    };
    await DbContext!.CleanPlaylistJobs.AddAsync(job);
    await DbContext!.SaveChangesAsync();

    // Mock track data
    var tracks = new List<SpotifyTrack>
    {
      new SpotifyTrack { Id = "track1", Name = "Song 1", Explicit = true, Artists = new[] { new SpotifyArtist { Id = "artist1", Name = "Artist 1" } }, Album = new SpotifyAlbum { Id = "album1", Name = "Album 1" }, Uri = "spotify:track:track1" },
      new SpotifyTrack { Id = "track2", Name = "Song 2", Explicit = false, Artists = new[] { new SpotifyArtist { Id = "artist2", Name = "Artist 2" } }, Album = new SpotifyAlbum { Id = "album2", Name = "Album 2" }, Uri = "spotify:track:track2" }
    };

    var cleanTrack = new SpotifyTrack { Id = "clean-track1", Name = "Song 1", Explicit = false, Uri = "spotify:track:clean1", Artists = new[] { new SpotifyArtist { Id = "artist1", Name = "Artist 1" } }, Album = new SpotifyAlbum { Id = "album1", Name = "Album 1" } };
    var newPlaylist = new SpotifyPlaylist { Id = "new-playlist-123", Name = "Clean Test", Tracks = new SpotifyPlaylistTracksRef { Total = 0, Href = "" }, Owner = new SpotifyUser { Id = "user123" } };

    _spotifyServiceMock.Setup(s => s.GetPlaylistTracksAsync(userId, "playlist-123")).ReturnsAsync(tracks);
    _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(userId, tracks[0])).ReturnsAsync(cleanTrack);
    _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(userId, tracks[1])).ReturnsAsync(tracks[1]); // Already clean
    _spotifyServiceMock.Setup(s => s.CreatePlaylistAsync(userId, "Clean Test", "Cleaned by RadioWash.")).ReturnsAsync(newPlaylist);
    _spotifyServiceMock.Setup(s => s.AddTracksToPlaylistAsync(userId, "new-playlist-123", It.IsAny<List<string>>())).Returns(Task.CompletedTask);

    // Act
    await _sut.ProcessJobAsync(job.Id);

    // Assert
    var updatedJob = await DbContext!.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(updatedJob);
    Assert.Equal(JobStatus.Completed, updatedJob.Status);
    Assert.Equal("new-playlist-123", updatedJob.TargetPlaylistId);
    Assert.Equal(2, updatedJob.ProcessedTracks);
    Assert.Equal(2, updatedJob.MatchedTracks);

    // Verify track mappings were created
    var mappings = await DbContext!.TrackMappings.Where(tm => tm.JobId == job.Id).ToListAsync();
    Assert.Equal(2, mappings.Count);
    Assert.True(mappings.All(m => m.HasCleanMatch));

    // Verify Spotify service calls
    _spotifyServiceMock.Verify(s => s.CreatePlaylistAsync(userId, "Clean Test", "Cleaned by RadioWash."), Times.Once);
    _spotifyServiceMock.Verify(s => s.AddTracksToPlaylistAsync(userId, "new-playlist-123", It.IsAny<List<string>>()), Times.Once);
  }

  [Fact]
  public async Task ProcessJobAsync_WithNoCleanMatches_ShouldStillCreatePlaylist()
  {
    // Arrange
    var userId = 1;
    var user = new User { Id = userId, SupabaseId = "user-123", DisplayName = "Test User", Email = "test@test.com" };
    await DbContext!.Users.AddAsync(user);

    var job = new CleanPlaylistJob
    {
      Id = 1,
      UserId = userId,
      SourcePlaylistId = "playlist-123",
      SourcePlaylistName = "Test Playlist",
      TargetPlaylistName = "Clean Test",
      Status = JobStatus.Pending,
      TotalTracks = 1
    };
    await DbContext!.CleanPlaylistJobs.AddAsync(job);
    await DbContext!.SaveChangesAsync();

    var explicitTrack = new SpotifyTrack { Id = "track1", Name = "Explicit Song", Explicit = true, Artists = new[] { new SpotifyArtist { Id = "artist1", Name = "Artist 1" } }, Album = new SpotifyAlbum { Id = "album1", Name = "Album 1" }, Uri = "spotify:track:track1" };
    var newPlaylist = new SpotifyPlaylist { Id = "new-playlist-123", Name = "Clean Test", Tracks = new SpotifyPlaylistTracksRef { Total = 0, Href = "" }, Owner = new SpotifyUser { Id = "user123" } };

    _spotifyServiceMock.Setup(s => s.GetPlaylistTracksAsync(userId, "playlist-123")).ReturnsAsync(new List<SpotifyTrack> { explicitTrack });
    _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(userId, explicitTrack)).ReturnsAsync((SpotifyTrack?)null); // No clean version found
    _spotifyServiceMock.Setup(s => s.CreatePlaylistAsync(userId, "Clean Test", "Cleaned by RadioWash.")).ReturnsAsync(newPlaylist);

    // Act
    await _sut.ProcessJobAsync(job.Id);

    // Assert
    var updatedJob = await DbContext!.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(updatedJob);
    Assert.Equal(JobStatus.Completed, updatedJob.Status);
    Assert.Equal("new-playlist-123", updatedJob.TargetPlaylistId);
    Assert.Equal(1, updatedJob.ProcessedTracks);
    Assert.Equal(0, updatedJob.MatchedTracks);

    // Verify playlist was created but AddTracks was not called (empty playlist)
    _spotifyServiceMock.Verify(s => s.CreatePlaylistAsync(userId, "Clean Test", "Cleaned by RadioWash."), Times.Once);
    _spotifyServiceMock.Verify(s => s.AddTracksToPlaylistAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never);
  }


}
