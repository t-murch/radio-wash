using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
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
    private readonly Mock<ISpotifyService> _mockSpotifyService;
    private readonly Mock<IJobOrchestrator> _mockJobOrchestrator;
    private readonly Mock<ILogger<CleanPlaylistService>> _mockLogger;
    private readonly Mock<ICleanPlaylistJobRepository> _mockJobRepo;
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly CleanPlaylistService _service;

    public CleanPlaylistServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockSpotifyService = new Mock<ISpotifyService>();
        _mockJobOrchestrator = new Mock<IJobOrchestrator>();
        _mockLogger = new Mock<ILogger<CleanPlaylistService>>();
        _mockJobRepo = new Mock<ICleanPlaylistJobRepository>();
        _mockUserRepo = new Mock<IUserRepository>();

        _mockUnitOfWork.Setup(x => x.Jobs).Returns(_mockJobRepo.Object);
        _mockUnitOfWork.Setup(x => x.Users).Returns(_mockUserRepo.Object);

        _service = new CleanPlaylistService(
            _mockUnitOfWork.Object,
            _mockSpotifyService.Object,
            _mockJobOrchestrator.Object,
            _mockLogger.Object);
    }

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
        var playlists = new List<PlaylistDto>
        {
            new PlaylistDto
            {
                Id = playlistId,
                Name = "Original Playlist",
                TrackCount = 50
            }
        };

        _mockUserRepo.Setup(x => x.GetByIdAsync(userId))
            .ReturnsAsync(user);
        _mockSpotifyService.Setup(x => x.GetUserPlaylistsAsync(userId))
            .ReturnsAsync(playlists);
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
        var playlists = new List<PlaylistDto>(); // Empty list

        _mockUserRepo.Setup(x => x.GetByIdAsync(userId))
            .ReturnsAsync(user);
        _mockSpotifyService.Setup(x => x.GetUserPlaylistsAsync(userId))
            .ReturnsAsync(playlists);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.CreateJobAsync(userId, createDto));

        _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
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