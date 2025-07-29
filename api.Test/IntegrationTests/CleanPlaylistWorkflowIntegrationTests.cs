using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using RadioWash.Api.Models.Spotify;
using Hangfire;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace RadioWash.Api.Test.IntegrationTests;

public class CleanPlaylistWorkflowIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;
    private readonly RadioWashDbContext _dbContext;
    private readonly Mock<ISpotifyService> _spotifyServiceMock;

    public CleanPlaylistWorkflowIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _spotifyServiceMock = new Mock<ISpotifyService>();
        
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove the real SpotifyService and replace with mock
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISpotifyService));
                if (descriptor != null)
                    services.Remove(descriptor);
                
                services.AddSingleton(_spotifyServiceMock.Object);
                
                // Use in-memory database for testing
                services.Remove(services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<RadioWashDbContext>))!);
                services.AddDbContext<RadioWashDbContext>(options =>
                {
                    options.UseInMemoryDatabase("IntegrationTestDb_" + Guid.NewGuid());
                });
                
                // Add test authentication
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationSchemeHandler>("Test", options => { });
            });
        });

        _client = _factory.CreateClient();
        _scope = _factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
        
        // Seed test data
        SeedTestData().Wait();
    }

    private async Task SeedTestData()
    {
        var testUser = new User
        {
            Id = 1,
            SupabaseId = "test-user-supabase-id",
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        
        await _dbContext.Users.AddAsync(testUser);
        await _dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task CompleteWorkflow_CreateCleanPlaylistJob_ShouldProcessSuccessfully()
    {
        // Arrange
        var sourcePlaylistId = "test-playlist-123";
        var targetPlaylistName = "My Clean Playlist";
        
        // Mock Spotify service responses
        var sourcePlaylist = new PlaylistDto
        {
            Id = sourcePlaylistId,
            Name = "Original Playlist",
            TrackCount = 3
        };
        
        var tracks = new List<SpotifyTrack>
        {
            new SpotifyTrack 
            { 
                Id = "track1", 
                Name = "Explicit Song", 
                Explicit = true, 
                Artists = new[] { new SpotifyArtist { Id = "artist1", Name = "Artist 1" } },
                Album = new SpotifyAlbum { Id = "album1", Name = "Album 1" },
                Uri = "spotify:track:track1"
            },
            new SpotifyTrack 
            { 
                Id = "track2", 
                Name = "Clean Song", 
                Explicit = false, 
                Artists = new[] { new SpotifyArtist { Id = "artist2", Name = "Artist 2" } },
                Album = new SpotifyAlbum { Id = "album2", Name = "Album 2" },
                Uri = "spotify:track:track2"
            },
            new SpotifyTrack 
            { 
                Id = "track3", 
                Name = "Another Explicit", 
                Explicit = true, 
                Artists = new[] { new SpotifyArtist { Id = "artist3", Name = "Artist 3" } },
                Album = new SpotifyAlbum { Id = "album3", Name = "Album 3" },
                Uri = "spotify:track:track3"
            }
        };
        
        var cleanTrack1 = new SpotifyTrack
        {
            Id = "clean-track1",
            Name = "Explicit Song",
            Explicit = false,
            Artists = tracks[0].Artists,
            Album = tracks[0].Album,
            Uri = "spotify:track:clean-track1"
        };
        
        var newPlaylist = new SpotifyPlaylist
        {
            Id = "new-clean-playlist-456",
            Name = targetPlaylistName,
            Tracks = new SpotifyPlaylistTracksRef { Total = 0, Href = "" },
            Owner = new SpotifyUser { Id = "test-user" }
        };
        
        // Setup Spotify service mocks
        _spotifyServiceMock.Setup(s => s.GetUserPlaylistsAsync(1))
            .ReturnsAsync(new List<PlaylistDto> { sourcePlaylist });
            
        _spotifyServiceMock.Setup(s => s.GetPlaylistTracksAsync(1, sourcePlaylistId))
            .ReturnsAsync(tracks);
            
        // Mock clean version finding
        _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(1, tracks[0]))
            .ReturnsAsync(cleanTrack1); // Found clean version
        _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(1, tracks[1]))
            .ReturnsAsync(tracks[1]); // Already clean
        _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(1, tracks[2]))
            .ReturnsAsync((SpotifyTrack?)null); // No clean version found
            
        _spotifyServiceMock.Setup(s => s.CreatePlaylistAsync(1, targetPlaylistName, "Cleaned by RadioWash."))
            .ReturnsAsync(newPlaylist);
            
        _spotifyServiceMock.Setup(s => s.AddTracksToPlaylistAsync(1, newPlaylist.Id, It.IsAny<List<string>>()))
            .Returns(Task.CompletedTask);

        var createJobRequest = new CreateCleanPlaylistJobDto
        {
            SourcePlaylistId = sourcePlaylistId,
            TargetPlaylistName = targetPlaylistName
        };

        // Add test authentication header
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

        // Act - Create the job via API
        var createResponse = await _client.PostAsJsonAsync("/api/cleanplaylist", createJobRequest);

        // Assert - Job creation successful
        createResponse.EnsureSuccessStatusCode();
        var jobDto = await createResponse.Content.ReadFromJsonAsync<CleanPlaylistJobDto>();
        
        Assert.NotNull(jobDto);
        Assert.Equal(sourcePlaylistId, jobDto.SourcePlaylistId);
        Assert.Equal(targetPlaylistName, jobDto.TargetPlaylistName);
        Assert.Equal(JobStatus.Pending, jobDto.Status);
        Assert.Equal(3, jobDto.TotalTracks);

        // Verify job was persisted to database
        var jobInDb = await _dbContext.CleanPlaylistJobs.FindAsync(jobDto.Id);
        Assert.NotNull(jobInDb);
        Assert.Equal(JobStatus.Pending, jobInDb.Status);

        // Act - Simulate background job processing
        var cleanPlaylistService = _scope.ServiceProvider.GetRequiredService<ICleanPlaylistService>();
        await cleanPlaylistService.ProcessJobAsync(jobDto.Id);

        // Assert - Job completed successfully
        await _dbContext.Entry(jobInDb).ReloadAsync();
        Assert.Equal(JobStatus.Completed, jobInDb.Status);
        Assert.Equal(newPlaylist.Id, jobInDb.TargetPlaylistId);
        Assert.Equal(3, jobInDb.ProcessedTracks);
        Assert.Equal(2, jobInDb.MatchedTracks); // 2 clean tracks found (1 converted, 1 already clean)

        // Verify track mappings were created
        var trackMappings = await _dbContext.TrackMappings
            .Where(tm => tm.JobId == jobDto.Id)
            .ToListAsync();
            
        Assert.Equal(3, trackMappings.Count);
        
        // Verify specific track mappings
        var mapping1 = trackMappings.First(tm => tm.SourceTrackId == "track1");
        Assert.True(mapping1.IsExplicit);
        Assert.True(mapping1.HasCleanMatch);
        Assert.Equal("clean-track1", mapping1.TargetTrackId);
        
        var mapping2 = trackMappings.First(tm => tm.SourceTrackId == "track2");
        Assert.False(mapping2.IsExplicit);
        Assert.True(mapping2.HasCleanMatch);
        Assert.Equal("track2", mapping2.TargetTrackId);
        
        var mapping3 = trackMappings.First(tm => tm.SourceTrackId == "track3");
        Assert.True(mapping3.IsExplicit);
        Assert.False(mapping3.HasCleanMatch);
        Assert.Null(mapping3.TargetTrackId);

        // Verify Spotify API calls were made correctly
        _spotifyServiceMock.Verify(s => s.GetUserPlaylistsAsync(1), Times.Once);
        _spotifyServiceMock.Verify(s => s.GetPlaylistTracksAsync(1, sourcePlaylistId), Times.Once);
        _spotifyServiceMock.Verify(s => s.FindCleanVersionAsync(1, It.IsAny<SpotifyTrack>()), Times.Exactly(3));
        _spotifyServiceMock.Verify(s => s.CreatePlaylistAsync(1, targetPlaylistName, "Cleaned by RadioWash."), Times.Once);
        _spotifyServiceMock.Verify(s => s.AddTracksToPlaylistAsync(1, newPlaylist.Id, 
            It.Is<List<string>>(uris => uris.Count == 2 && 
                uris.Contains("spotify:track:clean-track1") && 
                uris.Contains("spotify:track:track2"))), Times.Once);
    }

    [Fact]
    public async Task CompleteWorkflow_WithLargePlaylist_ShouldProcessInBatches()
    {
        // Arrange
        var sourcePlaylistId = "large-playlist-789";
        var trackCount = 25; // Triggers batch processing
        
        var sourcePlaylist = new PlaylistDto
        {
            Id = sourcePlaylistId,
            Name = "Large Playlist",
            TrackCount = trackCount
        };
        
        // Create 25 test tracks
        var tracks = Enumerable.Range(1, trackCount).Select(i => new SpotifyTrack
        {
            Id = $"track{i}",
            Name = $"Song {i}",
            Explicit = i % 2 == 0, // Every other track is explicit
            Artists = new[] { new SpotifyArtist { Id = $"artist{i}", Name = $"Artist {i}" } },
            Album = new SpotifyAlbum { Id = $"album{i}", Name = $"Album {i}" },
            Uri = $"spotify:track:track{i}"
        }).ToList();
        
        var newPlaylist = new SpotifyPlaylist
        {
            Id = "large-clean-playlist",
            Name = "Clean Large Playlist",
            Tracks = new SpotifyPlaylistTracksRef { Total = 0, Href = "" },
            Owner = new SpotifyUser { Id = "test-user" }
        };
        
        // Setup mocks
        _spotifyServiceMock.Setup(s => s.GetUserPlaylistsAsync(1))
            .ReturnsAsync(new List<PlaylistDto> { sourcePlaylist });
            
        _spotifyServiceMock.Setup(s => s.GetPlaylistTracksAsync(1, sourcePlaylistId))
            .ReturnsAsync(tracks);
            
        // Mock clean version finding for each track
        foreach (var track in tracks)
        {
            if (track.Explicit)
            {
                var cleanVersion = new SpotifyTrack
                {
                    Id = $"clean-{track.Id}",
                    Name = track.Name,
                    Explicit = false,
                    Artists = track.Artists,
                    Album = track.Album,
                    Uri = $"spotify:track:clean-{track.Id}"
                };
                _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(1, track))
                    .ReturnsAsync(cleanVersion);
            }
            else
            {
                _spotifyServiceMock.Setup(s => s.FindCleanVersionAsync(1, track))
                    .ReturnsAsync(track); // Already clean
            }
        }
        
        _spotifyServiceMock.Setup(s => s.CreatePlaylistAsync(1, "Clean Large Playlist", "Cleaned by RadioWash."))
            .ReturnsAsync(newPlaylist);
            
        _spotifyServiceMock.Setup(s => s.AddTracksToPlaylistAsync(1, newPlaylist.Id, It.IsAny<List<string>>()))
            .Returns(Task.CompletedTask);

        var createJobRequest = new CreateCleanPlaylistJobDto
        {
            SourcePlaylistId = sourcePlaylistId,
            TargetPlaylistName = "Clean Large Playlist"
        };

        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

        // Act - Create and process job
        var createResponse = await _client.PostAsJsonAsync("/api/cleanplaylist", createJobRequest);
        createResponse.EnsureSuccessStatusCode();
        var jobDto = await createResponse.Content.ReadFromJsonAsync<CleanPlaylistJobDto>();

        var cleanPlaylistService = _scope.ServiceProvider.GetRequiredService<ICleanPlaylistService>();
        await cleanPlaylistService.ProcessJobAsync(jobDto!.Id);

        // Assert - Large playlist processed successfully
        var jobInDb = await _dbContext.CleanPlaylistJobs.FindAsync(jobDto.Id);
        Assert.NotNull(jobInDb);
        Assert.Equal(JobStatus.Completed, jobInDb.Status);
        Assert.Equal(trackCount, jobInDb.ProcessedTracks);
        Assert.Equal(trackCount, jobInDb.MatchedTracks); // All tracks should have clean versions

        // Verify all track mappings were created (tests batch processing)
        var trackMappings = await _dbContext.TrackMappings
            .Where(tm => tm.JobId == jobDto.Id)
            .ToListAsync();
            
        Assert.Equal(trackCount, trackMappings.Count);
        Assert.True(trackMappings.All(tm => tm.HasCleanMatch));

        // Verify playlist creation with all clean tracks
        _spotifyServiceMock.Verify(s => s.AddTracksToPlaylistAsync(1, newPlaylist.Id, 
            It.Is<List<string>>(uris => uris.Count == trackCount)), Times.Once);
    }

    [Fact]
    public async Task CompleteWorkflow_WhenJobFails_ShouldHandleErrorsCorrectly()
    {
        // Arrange
        var sourcePlaylistId = "failing-playlist";
        var sourcePlaylist = new PlaylistDto
        {
            Id = sourcePlaylistId,
            Name = "Failing Playlist",
            TrackCount = 1
        };
        
        _spotifyServiceMock.Setup(s => s.GetUserPlaylistsAsync(1))
            .ReturnsAsync(new List<PlaylistDto> { sourcePlaylist });
            
        _spotifyServiceMock.Setup(s => s.GetPlaylistTracksAsync(1, sourcePlaylistId))
            .ThrowsAsync(new InvalidOperationException("Spotify API failure"));

        var createJobRequest = new CreateCleanPlaylistJobDto
        {
            SourcePlaylistId = sourcePlaylistId,
            TargetPlaylistName = "Failed Playlist"
        };

        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

        // Act
        var createResponse = await _client.PostAsJsonAsync("/api/cleanplaylist", createJobRequest);
        createResponse.EnsureSuccessStatusCode();
        var jobDto = await createResponse.Content.ReadFromJsonAsync<CleanPlaylistJobDto>();

        var cleanPlaylistService = _scope.ServiceProvider.GetRequiredService<ICleanPlaylistService>();
        await cleanPlaylistService.ProcessJobAsync(jobDto!.Id);

        // Assert - Job failed gracefully
        var jobInDb = await _dbContext.CleanPlaylistJobs.FindAsync(jobDto.Id);
        Assert.NotNull(jobInDb);
        Assert.Equal(JobStatus.Failed, jobInDb.Status);
        Assert.NotNull(jobInDb.ErrorMessage);
        Assert.Contains("Spotify API failure", jobInDb.ErrorMessage);
        
        // Verify no playlist was created
        Assert.Null(jobInDb.TargetPlaylistId);
        
        // Verify no track mappings were created
        var trackMappings = await _dbContext.TrackMappings
            .Where(tm => tm.JobId == jobDto.Id)
            .ToListAsync();
        Assert.Empty(trackMappings);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _client.Dispose();
    }
}

// Test authentication handler for integration tests
public class TestAuthenticationSchemeHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthenticationSchemeHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.NameIdentifier, "test-user-supabase-id"),
            new Claim("sub", "test-user-supabase-id")
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}