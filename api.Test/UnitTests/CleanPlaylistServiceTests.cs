using Moq;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace RadioWash.Api.Test.UnitTests;

public class CleanPlaylistServiceTests
{
  private readonly Mock<ISpotifyService> _spotifyServiceMock;
  private readonly Mock<IBackgroundJobClient> _backgroundJobClientMock;
  private readonly RadioWashDbContext _dbContext;
  private readonly CleanPlaylistService _sut; // System Under Test

  public CleanPlaylistServiceTests()
  {
    // Setup in-memory database
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // Unique DB for each test run
        .Options;
    _dbContext = new RadioWashDbContext(options);

    // Setup mocks
    _spotifyServiceMock = new Mock<ISpotifyService>();
    _backgroundJobClientMock = new Mock<IBackgroundJobClient>();
    var loggerMock = new Mock<ILogger<CleanPlaylistService>>();

    // Instantiate the service with mocks
    _sut = new CleanPlaylistService(
        _dbContext,
        _spotifyServiceMock.Object,
        loggerMock.Object,
        _backgroundJobClientMock.Object
    );
  }

  [Fact(Skip = "Stubbing")]
  public async Task CreateJobAsync_WhenUserAndPlaylistExist_ShouldCreateAndEnqueueJob()
  {
    // Arrange
    var userId = 1;
    var supabaseId = "user-supabase-id-123";
    var playlistId = "spotify-playlist-id-456";

    // 1. Seed the database with a user
    var user = new User { Id = userId, SupabaseId = supabaseId, DisplayName = "Test User", Email = "test@test.com" };
    await _dbContext.Users.AddAsync(user);
    await _dbContext.SaveChangesAsync();

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
    var jobInDb = await _dbContext.CleanPlaylistJobs.FirstOrDefaultAsync();
    Assert.NotNull(jobInDb);
    Assert.Equal(userId, jobInDb.UserId);
    Assert.Equal(playlistId, jobInDb.SourcePlaylistId);

    // 3. Verify the background job was enqueued
    _backgroundJobClientMock.Verify(
        client => client.Enqueue<ICleanPlaylistService>(service => service.ProcessJobAsync(jobInDb.Id)),
        Times.Once
    );
  }

  [Fact(Skip = "Stubbing")]
  public async Task CreateJobAsync_WhenUserDoesNotExist_ShouldThrowKeyNotFoundException()
  {
    // Arrange
    var nonExistentUserId = 999;
    var createJobDto = new CreateCleanPlaylistJobDto { SourcePlaylistId = "any-playlist" };

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.CreateJobAsync(nonExistentUserId, createJobDto));
  }
}
