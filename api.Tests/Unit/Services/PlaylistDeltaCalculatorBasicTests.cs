using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class PlaylistDeltaCalculatorBasicTests
{
    private readonly PlaylistDeltaCalculator _deltaCalculator;

    public PlaylistDeltaCalculatorBasicTests()
    {
        var mockLogger = new Mock<ILogger<PlaylistDeltaCalculator>>();
        _deltaCalculator = new PlaylistDeltaCalculator(mockLogger.Object);
    }

    [Fact]
    public async Task CalculateDelta_WithEmptyLists_ShouldReturnEmptyDelta()
    {
        // Arrange
        var sourceTracks = new List<SpotifyTrack>();
        var targetTracks = new List<SpotifyTrack>();
        var existingMappings = new List<TrackMapping>();

        // Act
        var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

        // Assert
        Assert.NotNull(delta);
        Assert.Empty(delta.TracksToAdd);
        Assert.Empty(delta.TracksToRemove);
        Assert.Empty(delta.NewTracks);
        Assert.Empty(delta.DesiredTrackOrder);
    }
}