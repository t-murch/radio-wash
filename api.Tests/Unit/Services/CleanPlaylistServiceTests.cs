using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for CleanPlaylistService
/// Demonstrates comprehensive testing with mocked dependencies
/// </summary>
public class CleanPlaylistServiceTests
{
  private readonly Mock<IUnitOfWork> _mockUnitOfWork;
  private readonly Mock<IMusicServiceFactory> _mockMusicServiceFactory;
  private readonly Mock<IMusicService> _mockMusicService;
  private readonly Mock<IMusicTokenService> _mockMusicTokenService;
  private readonly Mock<IJobOrchestrator> _mockJobOrchestrator;
  private readonly Mock<ILogger<CleanPlaylistService>> _mockLogger;
  private readonly Mock<ICleanPlaylistJobRepository> _mockJobRepo;
  private readonly Mock<IUserRepository> _mockUserRepo;
  private readonly CleanPlaylistService _service;

  public CleanPlaylistServiceTests()
  {
    _mockUnitOfWork = new Mock<IUnitOfWork>();
    _mockMusicServiceFactory = new Mock<IMusicServiceFactory>();
    _mockMusicService = new Mock<IMusicService>();
    _mockMusicTokenService = new Mock<IMusicTokenService>();
    _mockJobOrchestrator = new Mock<IJobOrchestrator>();
    _mockLogger = new Mock<ILogger<CleanPlaylistService>>();
    _mockJobRepo = new Mock<ICleanPlaylistJobRepository>();
    _mockUserRepo = new Mock<IUserRepository>();

    _mockUnitOfWork.Setup(x => x.Jobs).Returns(_mockJobRepo.Object);
    _mockUnitOfWork.Setup(x => x.Users).Returns(_mockUserRepo.Object);

    // Default: the factory resolves every supported provider to the shared mock, and the
    // user is connected. Individual tests override these.
    _mockMusicServiceFactory.Setup(x => x.GetService(It.IsAny<string>()))
        .Returns(_mockMusicService.Object);
    _mockMusicTokenService.Setup(x => x.HasValidTokensAsync(It.IsAny<int>(), It.IsAny<string>()))
        .ReturnsAsync(true);

    _service = new CleanPlaylistService(
        _mockUnitOfWork.Object,
        _mockMusicServiceFactory.Object,
        _mockMusicTokenService.Object,
        _mockJobOrchestrator.Object,
        _mockLogger.Object);
  }

  private static IReadOnlyList<PlaylistSummary> SinglePlaylist(string playlistId, string name = "Original Playlist", int trackCount = 50) =>
      new List<PlaylistSummary> { new(playlistId, name, null, null, trackCount, "owner", null) };

  [Fact]
  public async Task CreateJobAsync_WithValidUser_CreatesJobSuccessfully()
  {
    // Arrange
    var userId = 1;
    var playlistId = "playlist123";
    var createDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = playlistId,
      TargetPlaylistName = "Clean Playlist"
    };

    var user = new User { Id = userId, SupabaseId = "sb123" };

    _mockUserRepo.Setup(x => x.GetByIdAsync(userId))
        .ReturnsAsync(user);
    _mockMusicService.Setup(x => x.GetUserPlaylistsAsync(userId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(SinglePlaylist(playlistId));
    _mockJobRepo.Setup(x => x.CreateAsync(It.IsAny<CleanPlaylistJob>()))
        .ReturnsAsync(new CleanPlaylistJob { Id = 1 });
    _mockJobOrchestrator.Setup(x => x.EnqueueJobAsync(It.IsAny<int>()))
        .ReturnsAsync("hangfire123");

    // Act
    var result = await _service.CreateJobAsync(userId, createDto);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(playlistId, result.SourcePlaylistId);
    Assert.Equal("Clean Playlist", result.TargetPlaylistName);

    _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
    _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
    _mockJobOrchestrator.Verify(x => x.EnqueueJobAsync(It.IsAny<int>()), Times.Once);
  }

  [Fact]
  public async Task CreateJobAsync_WithInvalidUser_ThrowsKeyNotFoundException()
  {
    // Arrange
    var userId = 999;
    var createDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "playlist123"
    };

    _mockUserRepo.Setup(x => x.GetByIdAsync(userId))
        .ReturnsAsync((User?)null);

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(
        () => _service.CreateJobAsync(userId, createDto));

    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
    _mockJobOrchestrator.Verify(x => x.EnqueueJobAsync(It.IsAny<int>()), Times.Never);
  }

  [Fact]
  public async Task CreateJobAsync_WithInvalidPlaylist_ThrowsKeyNotFoundException()
  {
    // Arrange
    var userId = 1;
    var playlistId = "invalid123";
    var createDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = playlistId
    };

    var user = new User { Id = userId };

    _mockUserRepo.Setup(x => x.GetByIdAsync(userId))
        .ReturnsAsync(user);
    _mockMusicService.Setup(x => x.GetUserPlaylistsAsync(userId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PlaylistSummary>()); // Empty list

    // Act & Assert
    await Assert.ThrowsAsync<KeyNotFoundException>(
        () => _service.CreateJobAsync(userId, createDto));

    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
  }

  [Fact]
  public async Task CreateJobAsync_WithNoProvider_DefaultsToSpotify()
  {
    var userId = 1;
    var playlistId = "playlist123";
    var createDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = playlistId
    };
    var user = new User { Id = userId, SupabaseId = "sb123" };

    _mockUserRepo.Setup(x => x.GetByIdAsync(userId)).ReturnsAsync(user);
    _mockMusicService.Setup(x => x.GetUserPlaylistsAsync(userId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(SinglePlaylist(playlistId));
    _mockJobRepo.Setup(x => x.CreateAsync(It.IsAny<CleanPlaylistJob>()))
      .ReturnsAsync(new CleanPlaylistJob { Id = 1 });
    _mockJobOrchestrator.Setup(x => x.EnqueueJobAsync(It.IsAny<int>())).ReturnsAsync("hangfire123");

    var result = await _service.CreateJobAsync(userId, createDto);

    Assert.Equal("spotify", result.Provider);
    _mockMusicServiceFactory.Verify(x => x.GetService("spotify"), Times.Once);
    _mockJobRepo.Verify(x => x.CreateAsync(It.Is<CleanPlaylistJob>(job => job.Provider == "spotify")), Times.Once);
  }

  [Fact]
  public async Task CreateJobAsync_WithMixedCaseSpotifyProvider_NormalizesProvider()
  {
    var userId = 1;
    var playlistId = "playlist123";
    var createDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = playlistId,
      Provider = "Spotify"
    };
    var user = new User { Id = userId, SupabaseId = "sb123" };

    _mockUserRepo.Setup(x => x.GetByIdAsync(userId)).ReturnsAsync(user);
    _mockMusicService.Setup(x => x.GetUserPlaylistsAsync(userId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(SinglePlaylist(playlistId));
    _mockJobRepo.Setup(x => x.CreateAsync(It.IsAny<CleanPlaylistJob>()))
      .ReturnsAsync(new CleanPlaylistJob { Id = 1 });
    _mockJobOrchestrator.Setup(x => x.EnqueueJobAsync(It.IsAny<int>())).ReturnsAsync("hangfire123");

    var result = await _service.CreateJobAsync(userId, createDto);

    Assert.Equal("spotify", result.Provider);
    _mockJobRepo.Verify(x => x.CreateAsync(It.Is<CleanPlaylistJob>(job => job.Provider == "spotify")), Times.Once);
  }

  [Fact]
  public async Task CreateJobAsync_WithAppleMusicProvider_RoutesThroughFactory()
  {
    var userId = 1;
    var playlistId = "p.abc123";
    var createDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = playlistId,
      Provider = "apple_music"
    };
    var user = new User { Id = userId, SupabaseId = "sb123" };

    _mockUserRepo.Setup(x => x.GetByIdAsync(userId)).ReturnsAsync(user);
    _mockMusicService.Setup(x => x.GetUserPlaylistsAsync(userId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(SinglePlaylist(playlistId, "Apple Playlist"));
    _mockJobRepo.Setup(x => x.CreateAsync(It.IsAny<CleanPlaylistJob>()))
      .ReturnsAsync(new CleanPlaylistJob { Id = 1 });
    _mockJobOrchestrator.Setup(x => x.EnqueueJobAsync(It.IsAny<int>())).ReturnsAsync("hangfire123");

    var result = await _service.CreateJobAsync(userId, createDto);

    Assert.Equal("apple_music", result.Provider);
    _mockMusicServiceFactory.Verify(x => x.GetService("apple_music"), Times.Once);
    _mockMusicTokenService.Verify(x => x.HasValidTokensAsync(userId, "apple_music"), Times.Once);
    _mockJobRepo.Verify(x => x.CreateAsync(It.Is<CleanPlaylistJob>(job => job.Provider == "apple_music")), Times.Once);
  }

  [Fact]
  public async Task CreateJobAsync_WithoutValidTokens_FailsFastBeforePlaylistLookup()
  {
    var userId = 1;
    var createDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "playlist123",
      Provider = "apple_music"
    };
    var user = new User { Id = userId, SupabaseId = "sb123" };

    _mockUserRepo.Setup(x => x.GetByIdAsync(userId)).ReturnsAsync(user);
    _mockMusicTokenService.Setup(x => x.HasValidTokensAsync(userId, "apple_music"))
        .ReturnsAsync(false);

    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateJobAsync(userId, createDto));

    _mockMusicService.Verify(x => x.GetUserPlaylistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    _mockJobRepo.Verify(x => x.CreateAsync(It.IsAny<CleanPlaylistJob>()), Times.Never);
    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
  }

  [Fact]
  public async Task CreateJobAsync_WithUnsupportedProvider_ThrowsArgumentExceptionBeforeCreatingJob()
  {
    var userId = 1;
    var createDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "playlist123",
      Provider = "tidal"
    };
    var user = new User { Id = userId, SupabaseId = "sb123" };

    _mockUserRepo.Setup(x => x.GetByIdAsync(userId)).ReturnsAsync(user);

    var exception = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateJobAsync(userId, createDto));

    Assert.Equal("Provider 'tidal' is not supported.", exception.Message);
    _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Never);
    _mockMusicService.Verify(x => x.GetUserPlaylistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    _mockJobRepo.Verify(x => x.CreateAsync(It.IsAny<CleanPlaylistJob>()), Times.Never);
    _mockUnitOfWork.Verify(x => x.SaveChangesAsync(), Times.Never);
    _mockJobOrchestrator.Verify(x => x.EnqueueJobAsync(It.IsAny<int>()), Times.Never);
    _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Never);
  }

  [Fact]
  public async Task GetJobProgressAsync_WithValidJob_ReturnsProgress()
  {
    // Arrange
    var jobId = 1;
    var job = new CleanPlaylistJob
    {
      Id = jobId,
      ProcessedTracks = 25,
      TotalTracks = 50,
      CurrentBatch = "Batch 2",
      MatchedTracks = 20
    };

    _mockJobRepo.Setup(x => x.GetByIdAsync(jobId))
        .ReturnsAsync(job);

    // Act
    var result = await _service.GetJobProgressAsync(jobId);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(25, result.ProcessedTracks);
    Assert.Equal(50, result.TotalTracks);
    Assert.Equal(50, result.PercentComplete);
    Assert.Equal("Batch 2", result.CurrentBatch);
    Assert.Equal(20, result.MatchedTracks);
  }
}
