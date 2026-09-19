using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

public class PlaylistDeltaCalculatorTests
{
  private readonly PlaylistDeltaCalculator _deltaCalculator;
  private readonly Mock<ILogger<PlaylistDeltaCalculator>> _mockLogger;

  public PlaylistDeltaCalculatorTests()
  {
    _mockLogger = new Mock<ILogger<PlaylistDeltaCalculator>>();
    _deltaCalculator = new PlaylistDeltaCalculator(_mockLogger.Object);
  }

  [Fact]
  public async Task CalculateDelta_WithNewTracksInSource_ShouldIdentifyNewTracks()
  {
    // Arrange
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1", "Track 1"),
            CreateTrack("2", "Track 2"),
            CreateTrack("3", "Track 3") // New track
        };

    var targetTracks = new List<MusicTrack>
        {
            CreateTrack("clean-1", "Track 1 (Clean)"),
            CreateTrack("clean-2", "Track 2 (Clean)")
        };

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", "clean-1", true),
            CreateTrackMapping("2", "clean-2", true)
        };

    // Act
    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    // Assert
    Assert.Single(delta.NewTracks);
    Assert.Equal("3", delta.NewTracks[0].Id);
    Assert.Empty(delta.TracksToAdd); // No existing mappings for new tracks
    Assert.Empty(delta.TracksToRemove);
  }

  [Fact]
  public async Task CalculateDelta_WithMissingTracksInSource_ShouldIdentifyTracksToRemove()
  {
    // Arrange
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1", "Track 1")
            // Track 2 is missing from source
        };

    var targetTracks = new List<MusicTrack>
        {
            CreateTrack("clean-1", "Track 1 (Clean)"),
            CreateTrack("clean-2", "Track 2 (Clean)")
        };

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", "clean-1", true),
            CreateTrackMapping("2", "clean-2", true) // Source track 2 no longer exists
        };

    // Act
    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    // Assert
    Assert.Single(delta.TracksToRemove);
    Assert.Equal("clean-2", delta.TracksToRemove[0]);
    Assert.Empty(delta.NewTracks);
    Assert.Empty(delta.TracksToAdd);
  }

  [Fact]
  public async Task CalculateDelta_WithMappedTracksNotInTarget_ShouldIdentifyTracksToAdd()
  {
    // Arrange
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1", "Track 1"),
            CreateTrack("2", "Track 2")
        };

    var targetTracks = new List<MusicTrack>
        {
            CreateTrack("clean-1", "Track 1 (Clean)")
            // clean-2 is missing from target but has mapping
        };

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", "clean-1", true),
            CreateTrackMapping("2", "clean-2", true) // Target track not in playlist
        };

    // Act
    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    // Assert
    Assert.Single(delta.TracksToAdd);
    Assert.Equal("clean-2", delta.TracksToAdd[0]);
    Assert.Empty(delta.NewTracks);
    Assert.Empty(delta.TracksToRemove);
  }

  [Fact]
  public async Task CalculateDelta_WithNoChanges_ShouldReturnEmptyDelta()
  {
    // Arrange
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1", "Track 1"),
            CreateTrack("2", "Track 2")
        };

    var targetTracks = new List<MusicTrack>
        {
            CreateTrack("clean-1", "Track 1 (Clean)"),
            CreateTrack("clean-2", "Track 2 (Clean)")
        };

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", "clean-1", true),
            CreateTrackMapping("2", "clean-2", true)
        };

    // Act
    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    // Assert
    Assert.Empty(delta.TracksToAdd);
    Assert.Empty(delta.TracksToRemove);
    Assert.Empty(delta.NewTracks);
  }

  [Fact]
  public async Task CalculateDelta_WithMappingsWithoutCleanMatch_ShouldIgnoreMapping()
  {
    // Arrange
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1", "Track 1"),
            CreateTrack("2", "Track 2 Explicit")
        };

    var targetTracks = new List<MusicTrack>
        {
            CreateTrack("clean-1", "Track 1 (Clean)")
        };

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", "clean-1", true),
            CreateTrackMapping("2", null, false) // No clean match found
        };

    // Act
    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    // Assert
    Assert.Empty(delta.TracksToAdd);
    Assert.Empty(delta.TracksToRemove);
    Assert.Empty(delta.NewTracks);
  }

  [Fact]
  public async Task CalculateDelta_ShouldCalculateDesiredTrackOrder()
  {
    // Arrange
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1", "Track 1"),
            CreateTrack("2", "Track 2"),
            CreateTrack("3", "Track 3")
        };

    var targetTracks = new List<MusicTrack>
        {
            CreateTrack("clean-3", "Track 3 (Clean)"),
            CreateTrack("clean-1", "Track 1 (Clean)"),
            CreateTrack("clean-2", "Track 2 (Clean)")
        };

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", "clean-1", true),
            CreateTrackMapping("2", "clean-2", true),
            CreateTrackMapping("3", "clean-3", true)
        };

    // Act
    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    // Assert
    Assert.Equal(3, delta.DesiredTrackOrder.Count);
    Assert.Equal("clean-1", delta.DesiredTrackOrder[0]); // Track 1 should be first
    Assert.Equal("clean-2", delta.DesiredTrackOrder[1]); // Track 2 should be second
    Assert.Equal("clean-3", delta.DesiredTrackOrder[2]); // Track 3 should be third
  }

  [Fact]
  public async Task CalculateDelta_WithEmptyLists_ShouldReturnEmptyDelta()
  {
    // Arrange
    var sourceTracks = new List<MusicTrack>();
    var targetTracks = new List<MusicTrack>();
    var existingMappings = new List<TrackMapping>();

    // Act
    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    // Assert
    Assert.Empty(delta.TracksToAdd);
    Assert.Empty(delta.TracksToRemove);
    Assert.Empty(delta.NewTracks);
    Assert.Empty(delta.DesiredTrackOrder);
  }

  [Fact]
  public async Task CalculateDelta_WithNullTargetTrackId_ShouldSkipMapping()
  {
    // Arrange
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1", "Track 1")
        };

    var targetTracks = new List<MusicTrack>();

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", null, true) // Mapping with null target track ID
        };

    // Act
    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    // Assert
    Assert.Empty(delta.TracksToAdd);
    Assert.Empty(delta.TracksToRemove);
    Assert.Empty(delta.NewTracks);
  }

  [Fact]
  public async Task CalculateDelta_WithDuplicateSourceMappings_ShouldNotThrowAndPreferUsableMatch()
  {
    // Regression: a song appearing twice in the source playlist produces two mappings with
    // the same SourceTrackId (catalog id), which used to crash ToDictionary with
    // "An item with the same key has already been added".
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1551205503", "Duplicated Song"),
            CreateTrack("1551205503", "Duplicated Song")
        };

    var targetTracks = new List<MusicTrack>();

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1551205503", null, false), // matchless duplicate first
            CreateTrackMapping("1551205503", "clean-1", true)
        };

    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    // The mapping with the usable clean match wins, and the target mirrors the source's
    // duplicate: one clean copy per source occurrence.
    Assert.Equal(new[] { "clean-1", "clean-1" }, delta.TracksToAdd);
    Assert.Empty(delta.NewTracks);
    Assert.Empty(delta.TracksToRemove);
  }

  [Fact]
  public async Task CalculateDelta_WithDuplicatedSourceTrack_ShouldTopUpTargetToSourceCount()
  {
    // The source holds a song twice but the target only has its clean version once:
    // exactly one more copy is due.
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1", "Duplicated Song"),
            CreateTrack("1", "Duplicated Song")
        };

    var targetTracks = new List<MusicTrack>
        {
            CreateTrack("clean-1", "Duplicated Song (Clean)")
        };

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", "clean-1", true)
        };

    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    Assert.Single(delta.TracksToAdd);
    Assert.Equal("clean-1", delta.TracksToAdd[0]);
    Assert.Empty(delta.NewTracks);
    Assert.Empty(delta.TracksToRemove);
  }

  [Fact]
  public async Task CalculateDelta_WithDuplicatedNewTrack_ShouldKeepEachOccurrence()
  {
    // A brand-new song the source holds twice surfaces once per occurrence so the sync
    // can mirror the duplicate after matching it once.
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("new-1", "New Song"),
            CreateTrack("new-1", "New Song")
        };

    var targetTracks = new List<MusicTrack>();
    var existingMappings = new List<TrackMapping>();

    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    Assert.Equal(2, delta.NewTracks.Count);
    Assert.All(delta.NewTracks, t => Assert.Equal("new-1", t.Id));
    Assert.Empty(delta.TracksToAdd);
    Assert.Empty(delta.TracksToRemove);
  }

  [Fact]
  public async Task CalculateDelta_WithDuplicateTargetMappings_ShouldKeepTargetWhileAnySourceRemains()
  {
    // Two distinct source tracks can share one clean version. The shared target must not be
    // flagged for removal while either source track is still in the playlist.
    var sourceTracks = new List<MusicTrack>
        {
            CreateTrack("1", "Track 1")
            // Track 2 has left the playlist
        };

    var targetTracks = new List<MusicTrack>
        {
            CreateTrack("clean-shared", "Shared Clean Version")
        };

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", "clean-shared", true),
            CreateTrackMapping("2", "clean-shared", true)
        };

    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    Assert.Empty(delta.TracksToRemove);
    Assert.Empty(delta.TracksToAdd);
    Assert.Empty(delta.NewTracks);
  }

  [Fact]
  public async Task CalculateDelta_WithDuplicateTargetMappings_ShouldRemoveTargetWhenAllSourcesGone()
  {
    var sourceTracks = new List<MusicTrack>();

    var targetTracks = new List<MusicTrack>
        {
            CreateTrack("clean-shared", "Shared Clean Version")
        };

    var existingMappings = new List<TrackMapping>
        {
            CreateTrackMapping("1", "clean-shared", true),
            CreateTrackMapping("2", "clean-shared", true)
        };

    var delta = await _deltaCalculator.CalculateDeltaAsync(sourceTracks, targetTracks, existingMappings);

    Assert.Single(delta.TracksToRemove);
    Assert.Equal("clean-shared", delta.TracksToRemove[0]);
  }

  private static MusicTrack CreateTrack(string id, string name, bool isExplicit = false)
  {
    return new MusicTrack(
        Id: id,
        Name: name,
        IsExplicit: isExplicit,
        Artists: new[] { new MusicArtist("Test Artist") });
  }

  private static TrackMapping CreateTrackMapping(string sourceTrackId, string? targetTrackId, bool hasCleanMatch)
  {
    return new TrackMapping
    {
      SourceTrackId = sourceTrackId,
      TargetTrackId = targetTrackId,
      HasCleanMatch = hasCleanMatch,
      JobId = 1,
      SourceTrackName = "Test Track",
      SourceArtistName = "Test Artist",
      CreatedAt = DateTime.UtcNow
    };
  }
}
