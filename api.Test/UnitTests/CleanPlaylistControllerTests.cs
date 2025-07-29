using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Controllers;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;
using System.Security.Claims;

namespace RadioWash.Api.Test.UnitTests;

public class CleanPlaylistControllerTests : IDisposable
{
  private readonly Mock<ICleanPlaylistService> _cleanPlaylistServiceMock;
  private readonly RadioWashDbContext _dbContext;
  private readonly Mock<ILogger<CleanPlaylistController>> _loggerMock;
  private readonly CleanPlaylistController _controller;

  public CleanPlaylistControllerTests()
  {
    // Setup in-memory database
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;
    _dbContext = new RadioWashDbContext(options);

    _cleanPlaylistServiceMock = new Mock<ICleanPlaylistService>();
    _loggerMock = new Mock<ILogger<CleanPlaylistController>>();

    _controller = new CleanPlaylistController(_cleanPlaylistServiceMock.Object, _dbContext, _loggerMock.Object);
  }

  private void SetupAuthenticatedUser(int userId, string supabaseId)
  {
    var user = new User { Id = userId, SupabaseId = supabaseId, DisplayName = "Test User", Email = "test@test.com" };
    _dbContext.Users.Add(user);
    _dbContext.SaveChanges();

    var claims = new[]
    {
      new Claim(ClaimTypes.NameIdentifier, supabaseId)
    };
    var identity = new ClaimsIdentity(claims, "TestAuth");
    var principal = new ClaimsPrincipal(identity);

    _controller.ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext { User = principal }
    };
  }

  [Fact]
  public async Task CreateCleanPlaylistJob_WhenUserAuthenticated_ShouldCreateJob()
  {
    // Arrange
    var userId = 1;
    var supabaseId = "user-123";
    SetupAuthenticatedUser(userId, supabaseId);

    var jobDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "playlist-123",
      TargetPlaylistName = "My Clean Mix"
    };

    var expectedResult = new CleanPlaylistJobDto
    {
      Id = 1,
      SourcePlaylistId = "playlist-123",
      TargetPlaylistName = "My Clean Mix",
      Status = JobStatus.Pending.ToString(),
      TotalTracks = 10
    };

    _cleanPlaylistServiceMock
        .Setup(s => s.CreateJobAsync(userId, jobDto))
        .ReturnsAsync(expectedResult);

    // Act
    var result = await _controller.CreateCleanPlaylistJob(userId, jobDto);

    // Assert
    var createdResult = Assert.IsType<CreatedAtActionResult>(result);
    Assert.Equal(nameof(_controller.GetJob), createdResult.ActionName);
    Assert.Equal(expectedResult, createdResult.Value);

    _cleanPlaylistServiceMock.Verify(s => s.CreateJobAsync(userId, jobDto), Times.Once);
  }

  [Fact]
  public async Task CreateCleanPlaylistJob_WhenUserIdMismatch_ShouldReturnForbid()
  {
    // Arrange
    var authenticatedUserId = 1;
    var requestedUserId = 2; // Different user ID
    SetupAuthenticatedUser(authenticatedUserId, "user-123");

    var jobDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "playlist-123",
      TargetPlaylistName = "My Clean Mix"
    };

    // Act
    var result = await _controller.CreateCleanPlaylistJob(requestedUserId, jobDto);

    // Assert
    Assert.IsType<ForbidResult>(result);
    _cleanPlaylistServiceMock.Verify(s => s.CreateJobAsync(It.IsAny<int>(), It.IsAny<CreateCleanPlaylistJobDto>()), Times.Never);
  }

  [Fact]
  public async Task CreateCleanPlaylistJob_WhenServiceThrowsKeyNotFoundException_ShouldReturnNotFound()
  {
    // Arrange
    var userId = 1;
    SetupAuthenticatedUser(userId, "user-123");

    var jobDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "non-existent-playlist",
      TargetPlaylistName = "My Clean Mix"
    };

    _cleanPlaylistServiceMock
        .Setup(s => s.CreateJobAsync(userId, jobDto))
        .ThrowsAsync(new KeyNotFoundException("Playlist not found"));

    // Act
    var result = await _controller.CreateCleanPlaylistJob(userId, jobDto);

    // Assert
    var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
    var response = notFoundResult.Value;
    Assert.Contains("Playlist not found", response!.ToString());
  }

  [Fact]
  public async Task CreateCleanPlaylistJob_WhenServiceThrowsException_ShouldReturnInternalServerError()
  {
    // Arrange
    var userId = 1;
    SetupAuthenticatedUser(userId, "user-123");

    var jobDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "playlist-123",
      TargetPlaylistName = "My Clean Mix"
    };

    _cleanPlaylistServiceMock
        .Setup(s => s.CreateJobAsync(userId, jobDto))
        .ThrowsAsync(new Exception("Database connection failed"));

    // Act
    var result = await _controller.CreateCleanPlaylistJob(userId, jobDto);

    // Assert
    var statusCodeResult = Assert.IsType<ObjectResult>(result);
    Assert.Equal(500, statusCodeResult.StatusCode);
    Assert.Equal("An internal error occurred.", statusCodeResult.Value);
  }

  [Fact]
  public async Task GetUserJobs_WhenUserHasJobs_ShouldReturnJobs()
  {
    // Arrange
    var userId = 1;
    SetupAuthenticatedUser(userId, "user-123");

    var jobs = new[]
    {
      new CleanPlaylistJob
      {
        Id = 1,
        UserId = userId,
        SourcePlaylistId = "playlist-1",
        SourcePlaylistName = "My Mix 1",
        TargetPlaylistName = "Clean - My Mix 1",
        Status = JobStatus.Completed,
        TotalTracks = 20,
        ProcessedTracks = 20,
        MatchedTracks = 18,
        CreatedAt = DateTime.UtcNow.AddDays(-1),
        UpdatedAt = DateTime.UtcNow.AddDays(-1)
      },
      new CleanPlaylistJob
      {
        Id = 2,
        UserId = userId,
        SourcePlaylistId = "playlist-2",
        SourcePlaylistName = "My Mix 2",
        TargetPlaylistName = "Clean - My Mix 2",
        Status = JobStatus.Processing,
        TotalTracks = 15,
        ProcessedTracks = 8,
        MatchedTracks = 7,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      }
    };

    await _dbContext.CleanPlaylistJobs.AddRangeAsync(jobs);
    await _dbContext.SaveChangesAsync();

    // Act
    var result = await _controller.GetUserJobs();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var returnedJobs = Assert.IsType<List<CleanPlaylistJobDto>>(okResult.Value);
    Assert.Equal(2, returnedJobs.Count);

    // Should be ordered by CreatedAt descending
    Assert.Equal(2, returnedJobs[0].Id); // Most recent first
    Assert.Equal(1, returnedJobs[1].Id);
  }

  [Fact]
  public async Task GetUserJobs_WhenUserHasNoJobs_ShouldReturnEmptyList()
  {
    // Arrange
    var userId = 1;
    SetupAuthenticatedUser(userId, "user-123");

    // Act
    var result = await _controller.GetUserJobs();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var returnedJobs = Assert.IsType<List<CleanPlaylistJobDto>>(okResult.Value);
    Assert.Empty(returnedJobs);
  }

  [Fact]
  public async Task GetJob_WhenJobExists_ShouldReturnJob()
  {
    // Arrange
    var userId = 1;
    var jobId = 1;
    SetupAuthenticatedUser(userId, "user-123");

    var job = new CleanPlaylistJob
    {
      Id = jobId,
      UserId = userId,
      SourcePlaylistId = "playlist-123",
      SourcePlaylistName = "My Mix",
      TargetPlaylistName = "Clean - My Mix",
      Status = JobStatus.Completed,
      TotalTracks = 10,
      ProcessedTracks = 10,
      MatchedTracks = 8,
      ErrorMessage = null,
      CreatedAt = DateTime.UtcNow.AddDays(-1),
      UpdatedAt = DateTime.UtcNow.AddDays(-1)
    };

    await _dbContext.CleanPlaylistJobs.AddAsync(job);
    await _dbContext.SaveChangesAsync();

    // Act
    var result = await _controller.GetJob(userId, jobId);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var returnedJob = Assert.IsType<CleanPlaylistJobDto>(okResult.Value);
    Assert.Equal(jobId, returnedJob.Id);
    Assert.Equal("My Mix", returnedJob.SourcePlaylistName);
  }

  [Fact]
  public async Task GetJob_WhenJobDoesNotExist_ShouldReturnNotFound()
  {
    // Arrange
    var userId = 1;
    var jobId = 999; // Non-existent job
    SetupAuthenticatedUser(userId, "user-123");

    // Act
    var result = await _controller.GetJob(userId, jobId);

    // Assert
    Assert.IsType<NotFoundResult>(result);
  }

  [Fact]
  public async Task GetJob_WhenUserIdMismatch_ShouldReturnForbid()
  {
    // Arrange
    var authenticatedUserId = 1;
    var requestedUserId = 2;
    var jobId = 1;
    SetupAuthenticatedUser(authenticatedUserId, "user-123");

    // Act
    var result = await _controller.GetJob(requestedUserId, jobId);

    // Assert
    Assert.IsType<ForbidResult>(result);
  }

  [Fact]
  public async Task GetJobTrackMappings_WhenJobExists_ShouldReturnTrackMappings()
  {
    // Arrange
    var userId = 1;
    var jobId = 1;
    SetupAuthenticatedUser(userId, "user-123");

    var job = new CleanPlaylistJob
    {
      Id = jobId,
      UserId = userId,
      SourcePlaylistId = "playlist-123",
      SourcePlaylistName = "My Mix",
      TargetPlaylistName = "Clean - My Mix",
      Status = JobStatus.Completed,
      TotalTracks = 2
    };

    var mappings = new[]
    {
      new TrackMapping
      {
        Id = 1,
        JobId = jobId,
        SourceTrackId = "track-1",
        SourceTrackName = "Song 1",
        SourceArtistName = "Artist 1",
        IsExplicit = true,
        HasCleanMatch = true,
        TargetTrackId = "clean-track-1",
        TargetTrackName = "Song 1 (Clean)",
        TargetArtistName = "Artist 1"
      },
      new TrackMapping
      {
        Id = 2,
        JobId = jobId,
        SourceTrackId = "track-2",
        SourceTrackName = "Song 2",
        SourceArtistName = "Artist 2",
        IsExplicit = true,
        HasCleanMatch = false,
        TargetTrackId = null,
        TargetTrackName = null,
        TargetArtistName = null
      }
    };

    await _dbContext.CleanPlaylistJobs.AddAsync(job);
    await _dbContext.TrackMappings.AddRangeAsync(mappings);
    await _dbContext.SaveChangesAsync();

    // Act
    var result = await _controller.GetJobTrackMappings(userId, jobId);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var returnedMappings = Assert.IsType<List<TrackMappingDto>>(okResult.Value);
    Assert.Equal(2, returnedMappings.Count);

    var firstMapping = returnedMappings[0];
    Assert.Equal("track-1", firstMapping.SourceTrackId);
    Assert.True(firstMapping.HasCleanMatch);
    Assert.Equal("clean-track-1", firstMapping.TargetTrackId);

    var secondMapping = returnedMappings[1];
    Assert.Equal("track-2", secondMapping.SourceTrackId);
    Assert.False(secondMapping.HasCleanMatch);
    Assert.Null(secondMapping.TargetTrackId);
  }

  [Fact]
  public async Task GetJobTrackMappings_WhenJobDoesNotExist_ShouldReturnNotFound()
  {
    // Arrange
    var userId = 1;
    var jobId = 999; // Non-existent job
    SetupAuthenticatedUser(userId, "user-123");

    // Act
    var result = await _controller.GetJobTrackMappings(userId, jobId);

    // Assert
    var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
    Assert.Equal("Job not found or you do not have access.", notFoundResult.Value);
  }

  [Fact]
  public async Task GetJobTrackMappings_WhenUserIdMismatch_ShouldReturnForbid()
  {
    // Arrange
    var authenticatedUserId = 1;
    var requestedUserId = 2;
    var jobId = 1;
    SetupAuthenticatedUser(authenticatedUserId, "user-123");

    // Act
    var result = await _controller.GetJobTrackMappings(requestedUserId, jobId);

    // Assert
    Assert.IsType<ForbidResult>(result);
  }

  [Fact]
  public async Task GetJobTrackMappings_WhenJobBelongsToAnotherUser_ShouldReturnNotFound()
  {
    // Arrange
    var userId = 1;
    var otherUserId = 2;
    var jobId = 1;
    SetupAuthenticatedUser(userId, "user-123");

    // Create a job for another user
    var job = new CleanPlaylistJob
    {
      Id = jobId,
      UserId = otherUserId, // Different user
      SourcePlaylistId = "playlist-123",
      SourcePlaylistName = "Other User's Mix",
      TargetPlaylistName = "Other User's Clean Mix", // Required field
      Status = JobStatus.Completed
    };

    await _dbContext.CleanPlaylistJobs.AddAsync(job);
    await _dbContext.SaveChangesAsync();

    // Act
    var result = await _controller.GetJobTrackMappings(userId, jobId);

    // Assert
    var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
    Assert.Equal("Job not found or you do not have access.", notFoundResult.Value);
  }

  public void Dispose()
  {
    _dbContext.Dispose();
  }
}