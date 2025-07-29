using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using Moq;
using Hangfire;

namespace RadioWash.Api.Test.IntegrationTests;

/// <summary>
/// Integration tests specifically for transaction behavior.
/// These tests use a real PostgreSQL database to verify transaction rollback behavior.
/// NOTE: Requires PostgreSQL test database - can be skipped if not available.
/// </summary>
public class TransactionIntegrationTests : IDisposable
{
    private readonly RadioWashDbContext _dbContext;
    private readonly Mock<ISpotifyService> _spotifyServiceMock;
    private readonly Mock<IBackgroundJobClient> _backgroundJobClientMock;
    private readonly Mock<ILogger<CleanPlaylistService>> _loggerMock;
    private readonly CleanPlaylistService _sut;

    public TransactionIntegrationTests()
    {
        // Use PostgreSQL for real transaction testing
        // Skip these tests if PostgreSQL is not available
        var connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING") 
            ?? "Host=localhost;Database=radiowash_test;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        _dbContext = new RadioWashDbContext(options);
        
        // Ensure database is created and clean
        try
        {
            _dbContext.Database.EnsureDeleted();
            _dbContext.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            // Skip tests if database is not available
            throw new SkipException($"PostgreSQL test database not available: {ex.Message}");
        }

        // Setup mocks
        _spotifyServiceMock = new Mock<ISpotifyService>();
        _backgroundJobClientMock = new Mock<IBackgroundJobClient>();
        _loggerMock = new Mock<ILogger<CleanPlaylistService>>();

        _sut = new CleanPlaylistService(
            _dbContext,
            _spotifyServiceMock.Object,
            _loggerMock.Object,
            _backgroundJobClientMock.Object
        );

        // Seed test data
        SeedTestData().Wait();
    }

    private async Task SeedTestData()
    {
        var testUser = new User
        {
            SupabaseId = "test-user-transaction",
            DisplayName = "Transaction Test User",
            Email = "transaction@test.com"
        };
        
        await _dbContext.Users.AddAsync(testUser);
        await _dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateJobAsync_WhenSpotifyServiceFails_ShouldRollbackTransaction()
    {
        // Arrange
        var user = await _dbContext.Users.FirstAsync();
        
        // Mock Spotify service to throw an exception
        _spotifyServiceMock
            .Setup(s => s.GetUserPlaylistsAsync(user.Id))
            .ThrowsAsync(new InvalidOperationException("Spotify API error"));

        var createJobDto = new CreateCleanPlaylistJobDto { SourcePlaylistId = "playlist-123" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateJobAsync(user.Id, createJobDto));

        // Assert - No job should be created in database due to transaction rollback
        var jobCount = await _dbContext.CleanPlaylistJobs.CountAsync();
        Assert.Equal(0, jobCount);

        // Assert - Background job should not be enqueued
        _backgroundJobClientMock.Verify(
            client => client.Enqueue<ICleanPlaylistService>(It.IsAny<System.Linq.Expressions.Expression<System.Action<ICleanPlaylistService>>>()), 
            Times.Never);
    }

    [Fact]
    public async Task ProcessJobAsync_WhenPlaylistCreationFails_ShouldRollbackCompletionTransaction()
    {
        // Arrange
        var user = await _dbContext.Users.FirstAsync();
        
        var job = new CleanPlaylistJob
        {
            UserId = user.Id,
            SourcePlaylistId = "playlist-123",
            SourcePlaylistName = "Test Playlist",
            TargetPlaylistName = "Clean Test",
            Status = JobStatus.Pending,
            TotalTracks = 2
        };
        await _dbContext.CleanPlaylistJobs.AddAsync(job);
        await _dbContext.SaveChangesAsync();

        var tracks = new List<SpotifyTrack>
        {
            new SpotifyTrack { Id = "track1", Name = "Song 1", Explicit = true, Artists = new[] { new SpotifyArtist { Id = "artist1", Name = "Artist 1" } }, Album = new SpotifyAlbum { Id = "album1", Name = "Album 1" }, Uri = "spotify:track:track1" },
            new SpotifyTrack { Id = "track2", Name = "Song 2", Explicit = true, Artists = new[] { new SpotifyArtist { Id = "artist2", Name = "Artist 2" } }, Album = new SpotifyAlbum { Id = "album2", Name = "Album 2" }, Uri = "spotify:track:track2" }
        };

        var cleanTrack1 = new SpotifyTrack { Id = "clean-track1", Name = "Song 1", Explicit = false, Uri = "spotify:track:clean1", Artists = tracks[0].Artists, Album = tracks[0].Album };
        var cleanTrack2 = new SpotifyTrack { Id = "clean-track2", Name = "Song 2", Explicit = false, Uri = "spotify:track:clean2", Artists = tracks[1].Artists, Album = tracks[1].Album };

        _spotifyServiceMock.Setup(s => s.GetPlaylistTracksAsync(user.Id, "playlist-123")).ReturnsAsync(tracks);
        _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(user.Id, tracks[0])).ReturnsAsync(cleanTrack1);
        _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(user.Id, tracks[1])).ReturnsAsync(cleanTrack2);
        
        // Mock playlist creation to fail
        _spotifyServiceMock.Setup(s => s.CreatePlaylistAsync(user.Id, "Clean Test", "Cleaned by RadioWash."))
            .ThrowsAsync(new InvalidOperationException("Spotify playlist creation failed"));

        // Act
        await _sut.ProcessJobAsync(job.Id);

        // Assert
        var updatedJob = await _dbContext.CleanPlaylistJobs.FindAsync(job.Id);
        Assert.NotNull(updatedJob);
        Assert.Equal(JobStatus.Failed, updatedJob.Status);
        Assert.Contains("Spotify playlist creation failed", updatedJob.ErrorMessage!);

        // Assert - Track mappings should still exist from successful batch transactions
        // (This is the key difference from in-memory database - real transactions work correctly)
        var mappings = await _dbContext.TrackMappings.Where(tm => tm.JobId == job.Id).ToListAsync();
        Assert.Equal(2, mappings.Count); // Track mappings were saved in previous successful batch transactions

        // Assert - Job should not have TargetPlaylistId set due to failed playlist creation
        Assert.Null(updatedJob.TargetPlaylistId);
    }

    [Fact]
    public async Task ProcessJobAsync_WithBatchFailure_ShouldRollbackBatchTransaction()
    {
        // Arrange
        var user = await _dbContext.Users.FirstAsync();
        
        var job = new CleanPlaylistJob
        {
            UserId = user.Id,
            SourcePlaylistId = "playlist-123",
            SourcePlaylistName = "Test Playlist",
            TargetPlaylistName = "Clean Test",
            Status = JobStatus.Pending,
            TotalTracks = 15 // More than batch size of 10 to trigger batch save
        };
        await _dbContext.CleanPlaylistJobs.AddAsync(job);
        await _dbContext.SaveChangesAsync();

        // Create 15 tracks to process
        var tracks = Enumerable.Range(1, 15).Select(i => new SpotifyTrack 
        { 
            Id = $"track{i}", 
            Name = $"Song {i}", 
            Explicit = true, 
            Artists = new[] { new SpotifyArtist { Id = $"artist{i}", Name = $"Artist {i}" } },
            Album = new SpotifyAlbum { Id = $"album{i}", Name = $"Album {i}" },
            Uri = $"spotify:track:track{i}"
        }).ToList();

        _spotifyServiceMock.Setup(s => s.GetPlaylistTracksAsync(user.Id, "playlist-123")).ReturnsAsync(tracks);
        
        // Force failure after the 10th track to simulate batch processing failure
        var callCount = 0;
        _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(user.Id, It.IsAny<SpotifyTrack>()))
            .ReturnsAsync((int uid, SpotifyTrack track) =>
            {
                callCount++;
                if (callCount == 12) // Fail during second batch
                {
                    throw new InvalidOperationException("Simulated API failure during batch processing");
                }
                return new SpotifyTrack 
                { 
                    Id = $"clean-{track.Id}", 
                    Name = track.Name, 
                    Explicit = false, 
                    Uri = $"spotify:track:clean-{track.Id}",
                    Artists = track.Artists,
                    Album = track.Album
                };
            });

        // Act
        await _sut.ProcessJobAsync(job.Id);

        // Assert
        var updatedJob = await _dbContext.CleanPlaylistJobs.FindAsync(job.Id);
        Assert.NotNull(updatedJob);
        Assert.Equal(JobStatus.Failed, updatedJob.Status);
        Assert.Contains("Simulated API failure", updatedJob.ErrorMessage!);

        // Assert - Only the first batch should be saved (10 mappings)
        // The second batch should be rolled back due to transaction failure
        var mappings = await _dbContext.TrackMappings.Where(tm => tm.JobId == job.Id).ToListAsync();
        Assert.Equal(10, mappings.Count); // Only first batch should be committed
    }

    [Fact]
    public async Task ProcessJobAsync_SuccessfulLargePlaylist_ShouldCommitAllBatches()
    {
        // Arrange
        var user = await _dbContext.Users.FirstAsync();
        
        var job = new CleanPlaylistJob
        {
            UserId = user.Id,
            SourcePlaylistId = "playlist-456",
            SourcePlaylistName = "Large Playlist",
            TargetPlaylistName = "Clean Large",
            Status = JobStatus.Pending,
            TotalTracks = 25 // Will trigger 3 batch saves (10, 10, 5)
        };
        await _dbContext.CleanPlaylistJobs.AddAsync(job);
        await _dbContext.SaveChangesAsync();

        // Create 25 tracks
        var tracks = Enumerable.Range(1, 25).Select(i => new SpotifyTrack 
        { 
            Id = $"track{i}", 
            Name = $"Song {i}", 
            Explicit = i % 2 == 0, // Half explicit, half clean
            Artists = new[] { new SpotifyArtist { Id = $"artist{i}", Name = $"Artist {i}" } },
            Album = new SpotifyAlbum { Id = $"album{i}", Name = $"Album {i}" },
            Uri = $"spotify:track:track{i}"
        }).ToList();

        var newPlaylist = new SpotifyPlaylist { Id = "new-playlist-789", Name = "Clean Large", Tracks = new SpotifyPlaylistTracksRef { Total = 0, Href = "" }, Owner = new SpotifyUser { Id = "user123" } };

        _spotifyServiceMock.Setup(s => s.GetPlaylistTracksAsync(user.Id, "playlist-456")).ReturnsAsync(tracks);
        
        // Mock clean version finding
        foreach (var track in tracks)
        {
            if (track.Explicit)
            {
                var cleanVersion = new SpotifyTrack 
                { 
                    Id = $"clean-{track.Id}", 
                    Name = track.Name, 
                    Explicit = false, 
                    Uri = $"spotify:track:clean-{track.Id}",
                    Artists = track.Artists,
                    Album = track.Album
                };
                _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(user.Id, track)).ReturnsAsync(cleanVersion);
            }
            else
            {
                _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(user.Id, track)).ReturnsAsync(track); // Already clean
            }
        }

        _spotifyServiceMock.Setup(s => s.CreatePlaylistAsync(user.Id, "Clean Large", "Cleaned by RadioWash.")).ReturnsAsync(newPlaylist);
        _spotifyServiceMock.Setup(s => s.AddTracksToPlaylistAsync(user.Id, "new-playlist-789", It.IsAny<List<string>>())).Returns(Task.CompletedTask);

        // Act
        await _sut.ProcessJobAsync(job.Id);

        // Assert
        var updatedJob = await _dbContext.CleanPlaylistJobs.FindAsync(job.Id);
        Assert.NotNull(updatedJob);
        Assert.Equal(JobStatus.Completed, updatedJob.Status);
        Assert.Equal("new-playlist-789", updatedJob.TargetPlaylistId);
        Assert.Equal(25, updatedJob.ProcessedTracks);
        Assert.Equal(25, updatedJob.MatchedTracks);

        // Verify all track mappings were created and committed across multiple batch transactions
        var mappings = await _dbContext.TrackMappings.Where(tm => tm.JobId == job.Id).ToListAsync();
        Assert.Equal(25, mappings.Count);
        Assert.True(mappings.All(m => m.HasCleanMatch));
    }

    public void Dispose()
    {
        try
        {
            _dbContext.Database.EnsureDeleted();
        }
        catch
        {
            // Ignore cleanup errors
        }
        _dbContext.Dispose();
    }
}

// Custom exception for skipping tests when database is not available
public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}