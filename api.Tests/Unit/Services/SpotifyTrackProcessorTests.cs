using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for SpotifyTrackProcessor
/// </summary>
public class SpotifyTrackProcessorTests
{
    private readonly Mock<ISpotifyService> _mockSpotifyService;
    private readonly Mock<ILogger<SpotifyTrackProcessor>> _mockLogger;
    private readonly SpotifyTrackProcessor _processor;

    public SpotifyTrackProcessorTests()
    {
        _mockSpotifyService = new Mock<ISpotifyService>();
        _mockLogger = new Mock<ILogger<SpotifyTrackProcessor>>();
        _processor = new SpotifyTrackProcessor(_mockSpotifyService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ProcessTrackAsync_ExplicitTrackWithCleanVersion_ReturnsCorrectMapping()
    {
        // Arrange
        var userId = 1;
        var jobId = 1;
        var track = new SpotifyTrack
        {
            Id = "track1",
            Name = "Explicit Song",
            Explicit = true,
            Artists = new[] { new SpotifyArtist { Name = "Artist 1" } }
        };

        var cleanTrack = new SpotifyTrack
        {
            Id = "clean1",
            Name = "Clean Song",
            Explicit = false,
            Artists = new[] { new SpotifyArtist { Name = "Artist 1" } }
        };

        _mockSpotifyService.Setup(x => x.FindCleanVersionAsync(userId, track))
            .ReturnsAsync(cleanTrack);

        // Act
        var result = await _processor.ProcessTrackAsync(userId, jobId, track);

        // Assert
        Assert.Equal("track1", result.SourceTrackId);
        Assert.Equal("Explicit Song", result.SourceTrackName);
        Assert.True(result.IsExplicit);
        Assert.True(result.HasCleanMatch);
        Assert.Equal("clean1", result.TargetTrackId);
        Assert.Equal("Clean Song", result.TargetTrackName);
    }

    [Fact]
    public async Task ProcessTrackAsync_CleanTrack_ReturnsOriginalAsTarget()
    {
        // Arrange
        var userId = 1;
        var jobId = 1;
        var track = new SpotifyTrack
        {
            Id = "track1",
            Name = "Clean Song",
            Explicit = false,
            Artists = new[] { new SpotifyArtist { Name = "Artist 1" } }
        };

        _mockSpotifyService.Setup(x => x.FindCleanVersionAsync(userId, track))
            .ReturnsAsync(track); // Returns same track if already clean

        // Act
        var result = await _processor.ProcessTrackAsync(userId, jobId, track);

        // Assert
        Assert.Equal("track1", result.SourceTrackId);
        Assert.False(result.IsExplicit);
        Assert.True(result.HasCleanMatch);
        Assert.Equal("track1", result.TargetTrackId);
    }
}