using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.AppleMusic;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Tests.Integration.TestHelpers;

namespace RadioWash.Api.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the cross-service copy pipeline, mirroring
/// <see cref="CleanPlaylistPipelineIntegrationTests"/>: a real PostgreSQL database
/// (Testcontainers) with canned provider stubs at the HTTP boundary, so the real adapters
/// (SpotifyMusicService / AppleMusicMusicService), the real TrackMatcher/PlaylistCopier,
/// and the real CleanPlaylistJobProcessor all execute against real persisted state.
///
/// Covers the three flows this feature adds: Spotify→Apple with the clean toggle on,
/// Apple→Spotify as a faithful 1:1 copy, and Apple→Apple clean (provider parity through
/// the cleaner path).
/// </summary>
public class PlaylistCopyPipelineIntegrationTests : PostgreSqlIntegrationTestBase
{
  private readonly FakeSpotifyService _fakeSpotify;
  private readonly FakeAppleMusicService _fakeApple;

  public PlaylistCopyPipelineIntegrationTests()
  {
    _fakeSpotify = _serviceProvider.GetRequiredService<FakeSpotifyService>();
    _fakeApple = _serviceProvider.GetRequiredService<FakeAppleMusicService>();
  }

  protected override void ConfigureServices(IServiceCollection services)
  {
    base.ConfigureServices(services);
    SeedSupabaseAuthSchemaStub();

    services.AddSingleton<FakeSpotifyService>();
    services.AddSingleton<ISpotifyService>(sp => sp.GetRequiredService<FakeSpotifyService>());
    services.AddSingleton<FakeAppleMusicService>();
    services.AddSingleton<IAppleMusicService>(sp => sp.GetRequiredService<FakeAppleMusicService>());

    services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
    services.AddScoped<IUserMusicTokenRepository, UserMusicTokenRepository>();
    services.AddScoped<IUserRepository, UserRepository>();
    services.AddScoped<ICleanPlaylistJobRepository, CleanPlaylistJobRepository>();
    services.AddScoped<ITrackMappingRepository, TrackMappingRepository>();
    services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
    services.AddScoped<IUserSubscriptionRepository, UserSubscriptionRepository>();
    services.AddScoped<IPlaylistSyncConfigRepository, PlaylistSyncConfigRepository>();
    services.AddScoped<IPlaylistSyncHistoryRepository, PlaylistSyncHistoryRepository>();
    services.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();

    services.AddScoped<IMusicTokenService, MusicTokenService>();
    services.AddScoped<IMusicTokenRefresher, SpotifyTokenRefresher>();
    services.AddHttpClient();

    services.AddScoped<SpotifyMusicService>();
    services.AddKeyedScoped<IMusicService>(
      SpotifyMusicService.Provider,
      (sp, _) => sp.GetRequiredService<SpotifyMusicService>());
    services.AddScoped<AppleMusicMusicService>();
    services.AddKeyedScoped<IMusicService>(
      AppleMusicMusicService.Provider,
      (sp, _) => sp.GetRequiredService<AppleMusicMusicService>());
    services.AddScoped<IMusicServiceFactory, MusicServiceFactory>();

    services.AddScoped<IProgressTracker, SmartProgressTracker>();
    services.AddSingleton(new BatchConfiguration());
    services.AddSingleton<IProgressBroadcastService, FakeProgressBroadcast>();

    services.AddScoped<IPlaylistCleanerFactory, PlaylistCleanerFactory>();
    services.AddScoped<ITrackMatcher, TrackMatcher>();
    services.AddScoped<IPlaylistCopier, PlaylistCopier>();
    services.AddScoped<ICleanPlaylistJobProcessor, CleanPlaylistJobProcessor>();

    services.AddDataProtection();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
  }

  [Fact]
  public async Task ProcessJob_SpotifyToAppleWithCleanToggle_MatchesViaIsrcAndSwapsExplicit()
  {
    var user = await SeedUserWithTokensAsync();

    // Source Spotify playlist: an explicit track whose Apple ISRC hit is explicit but has a
    // clean sibling findable via search; a clean track with a direct ISRC hit; and an
    // explicit track with no ISRC and no search results (unmatchable).
    _fakeSpotify.SourcePlaylistTracks["sp-src"] = new[]
    {
      MakeSpotifyTrack("e1", "Explicit Hit", isExplicit: true, isrc: "ISRC_E1"),
      MakeSpotifyTrack("c1", "Squeaky Clean", isExplicit: false, isrc: "ISRC_C1"),
      MakeSpotifyTrack("e2", "Unreleased Mix", isExplicit: true)
    };
    _fakeSpotify.UserPlaylists[user.Id] = new[]
    {
      new PlaylistDto { Id = "sp-src", Name = "Road Trip", TrackCount = 3, OwnerId = "sp-owner" }
    };

    _fakeApple.Catalog.Add(MakeCatalogSong("apl-e1", "Explicit Hit", "explicit", isrc: "ISRC_E1"));
    _fakeApple.Catalog.Add(MakeCatalogSong("apl-c1", "Squeaky Clean", "clean", isrc: "ISRC_C1"));
    // Clean sibling surfaces via catalog search (Apple has no explicit-exclusion operator).
    _fakeApple.SearchResultsByTerm["Explicit Hit Artist"] = new List<AppleCatalogSong>
    {
      MakeCatalogSong("apl-e1", "Explicit Hit", "explicit", isrc: "ISRC_E1"),
      MakeCatalogSong("apl-e1-clean", "Explicit Hit", "clean")
    };
    _fakeApple.CreatedPlaylistId = "p.target";

    var dto = await CreateJobAsync(user.Id, new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "sp-src",
      Provider = "spotify",
      TargetProvider = "apple_music",
      SwapExplicitForClean = true
    });
    await ProcessJobAsync(dto.Id);

    using var assertScope = _serviceProvider.CreateScope();
    var assertDb = assertScope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

    var job = await assertDb.CleanPlaylistJobs.FindAsync(dto.Id)
      ?? throw new Xunit.Sdk.XunitException("Job disappeared");
    Assert.True(job.Status == "Completed",
      $"Expected Completed, got {job.Status}. ErrorMessage: {job.ErrorMessage}");
    Assert.Equal("copy", job.JobType);
    Assert.Equal("p.target", job.TargetPlaylistId);
    Assert.Equal(3, job.ProcessedTracks);
    Assert.Equal(2, job.MatchedTracks);

    var mappings = assertDb.TrackMappings.Where(m => m.JobId == job.Id).OrderBy(m => m.Id).ToList();
    Assert.Equal(3, mappings.Count);
    var e1 = mappings.Single(m => m.SourceTrackId == "e1");
    Assert.Equal("isrc-clean", e1.MatchMethod);
    Assert.Equal("apl-e1-clean", e1.TargetTrackId);
    Assert.Equal("ISRC_E1", e1.Isrc);
    var c1 = mappings.Single(m => m.SourceTrackId == "c1");
    Assert.Equal("isrc", c1.MatchMethod);
    Assert.Equal("apl-c1", c1.TargetTrackId);
    var e2 = mappings.Single(m => m.SourceTrackId == "e2");
    Assert.Equal("none", e2.MatchMethod);
    Assert.False(e2.HasCleanMatch);

    // The Apple library playlist received the clean catalog ids, in order.
    var invocation = Assert.Single(_fakeApple.AddTracksInvocations);
    Assert.Equal("p.target", invocation.PlaylistId);
    Assert.Equal(new[] { "apl-e1-clean", "apl-c1" }, invocation.SongIds);
    // Copy playlists advertise their provenance.
    var created = Assert.Single(_fakeApple.CreatedPlaylists);
    Assert.Contains("Road Trip", created.Description);
  }

  [Fact]
  public async Task ProcessJob_AppleToSpotifyFaithfulCopy_ResolvesLibraryTracksThroughCatalog()
  {
    var user = await SeedUserWithTokensAsync();

    // Apple source playlist: one catalog-linked song (resolvable cross-catalog through its
    // ISRC) and one personal upload with no catalog linkage (must become an unmatched row,
    // never an error).
    _fakeApple.UserPlaylists[user.Id] = new List<AppleLibraryPlaylist>
    {
      new()
      {
        Id = "p.src",
        Attributes = new AppleLibraryPlaylistAttributes { Name = "Apple Road Trip", CanEdit = true }
      }
    };
    _fakeApple.PlaylistTracks["p.src"] = new List<AppleLibrarySong>
    {
      MakeLibrarySong("i.1", "Known Song", catalogId: "cat-a1", contentRating: "explicit"),
      MakeLibrarySong("i.2", "Home Recording")
    };
    _fakeApple.Catalog.Add(MakeCatalogSong("cat-a1", "Known Song", "explicit", isrc: "ISRC_A1"));

    _fakeSpotify.TracksByIsrc["ISRC_A1"] = MakeSpotifyTrack("sp-a1", "Known Song", isExplicit: true, isrc: "ISRC_A1");
    _fakeSpotify.CreatedPlaylistId = "sp-target";
    _fakeSpotify.UserProfile[user.Id] = new SpotifyUserProfile { Id = "sp-owner", DisplayName = "Owner" };

    var dto = await CreateJobAsync(user.Id, new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "p.src",
      Provider = "apple_music",
      TargetProvider = "spotify",
      SwapExplicitForClean = false
    });
    await ProcessJobAsync(dto.Id);

    using var assertScope = _serviceProvider.CreateScope();
    var assertDb = assertScope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

    var job = await assertDb.CleanPlaylistJobs.FindAsync(dto.Id)
      ?? throw new Xunit.Sdk.XunitException("Job disappeared");
    Assert.True(job.Status == "Completed",
      $"Expected Completed, got {job.Status}. ErrorMessage: {job.ErrorMessage}");
    // Faithful copies keep the source playlist name.
    Assert.Equal("Apple Road Trip", job.TargetPlaylistName);
    Assert.Equal(2, job.ProcessedTracks);
    Assert.Equal(1, job.MatchedTracks);

    var mappings = assertDb.TrackMappings.Where(m => m.JobId == job.Id).OrderBy(m => m.Id).ToList();
    Assert.Equal("isrc", mappings.Single(m => m.SourceTrackId == "cat-a1").MatchMethod);
    // The upload kept its library id and matched nothing.
    Assert.Equal("none", mappings.Single(m => m.SourceTrackId == "i.2").MatchMethod);

    // Spotify received the matched track as a spotify:track: URI via the real adapter.
    var invocation = Assert.Single(_fakeSpotify.AddTracksInvocations);
    Assert.Equal("sp-target", invocation.PlaylistId);
    Assert.Equal(new[] { "spotify:track:sp-a1" }, invocation.TrackUris);
  }

  [Fact]
  public async Task ProcessJob_AppleToAppleClean_RunsThroughCleanerWithAppleAdapter()
  {
    var user = await SeedUserWithTokensAsync();

    _fakeApple.UserPlaylists[user.Id] = new List<AppleLibraryPlaylist>
    {
      new()
      {
        Id = "p.clean-src",
        Attributes = new AppleLibraryPlaylistAttributes { Name = "My Mix", CanEdit = true }
      }
    };
    _fakeApple.PlaylistTracks["p.clean-src"] = new List<AppleLibrarySong>
    {
      MakeLibrarySong("i.10", "Bad Song", catalogId: "cat-x", contentRating: "explicit"),
      MakeLibrarySong("i.11", "Nice Song", catalogId: "cat-y", contentRating: "clean")
    };
    _fakeApple.Catalog.Add(MakeCatalogSong("cat-x", "Bad Song", "explicit", isrc: "ISRC_X"));
    _fakeApple.Catalog.Add(MakeCatalogSong("cat-y", "Nice Song", "clean", isrc: "ISRC_Y"));
    _fakeApple.SearchResultsByTerm["Bad Song Artist"] = new List<AppleCatalogSong>
    {
      MakeCatalogSong("cat-x-clean", "Bad Song (Clean)", "clean")
    };
    _fakeApple.CreatedPlaylistId = "p.cleaned";

    var dto = await CreateJobAsync(user.Id, new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "p.clean-src",
      Provider = "apple_music"
    });
    await ProcessJobAsync(dto.Id);

    using var assertScope = _serviceProvider.CreateScope();
    var assertDb = assertScope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

    var job = await assertDb.CleanPlaylistJobs.FindAsync(dto.Id)
      ?? throw new Xunit.Sdk.XunitException("Job disappeared");
    Assert.True(job.Status == "Completed",
      $"Expected Completed, got {job.Status}. ErrorMessage: {job.ErrorMessage}");
    Assert.Equal("clean", job.JobType);
    Assert.Equal("apple_music", job.Provider);
    Assert.Equal("Clean - My Mix", job.TargetPlaylistName);
    Assert.Equal(2, job.ProcessedTracks);
    Assert.Equal(2, job.MatchedTracks);

    var invocation = Assert.Single(_fakeApple.AddTracksInvocations);
    Assert.Equal("p.cleaned", invocation.PlaylistId);
    Assert.Equal(new[] { "cat-x-clean", "cat-y" }, invocation.SongIds);
  }

  // --- helpers ---

  private async Task<User> SeedUserWithTokensAsync()
  {
    var user = new User
    {
      SupabaseId = Guid.NewGuid().ToString(),
      DisplayName = "Copy Integration User",
      Email = "copy-integration@example.com",
      PrimaryProvider = "spotify",
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _dbContext.Users.Add(user);
    await _dbContext.SaveChangesAsync();

    var encryption = _serviceProvider.GetRequiredService<ITokenEncryptionService>();
    _dbContext.UserMusicTokens.Add(new UserMusicToken
    {
      UserId = user.Id,
      Provider = "spotify",
      EncryptedAccessToken = encryption.EncryptToken("fake-spotify-token"),
      EncryptedRefreshToken = encryption.EncryptToken("fake-spotify-refresh"),
      ExpiresAt = DateTime.UtcNow.AddHours(1),
      Scopes = "[\"playlist-modify-private\"]",
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    });
    // Apple: long-lived Music User Token, no refresh token — the real shape.
    _dbContext.UserMusicTokens.Add(new UserMusicToken
    {
      UserId = user.Id,
      Provider = "apple_music",
      EncryptedAccessToken = encryption.EncryptToken("fake-music-user-token"),
      EncryptedRefreshToken = null,
      ExpiresAt = DateTime.UtcNow.AddDays(150),
      Scopes = "[]",
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    });
    await _dbContext.SaveChangesAsync();
    return user;
  }

  private async Task<CleanPlaylistJobDto> CreateJobAsync(int userId, CreateCleanPlaylistJobDto dto)
  {
    using var scope = _serviceProvider.CreateScope();
    var service = new CleanPlaylistService(
      scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
      scope.ServiceProvider.GetRequiredService<IMusicServiceFactory>(),
      scope.ServiceProvider.GetRequiredService<IMusicTokenService>(),
      new NoopJobOrchestrator(),
      scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CleanPlaylistService>>());
    return await service.CreateJobAsync(userId, dto);
  }

  private async Task ProcessJobAsync(int jobId)
  {
    using var scope = _serviceProvider.CreateScope();
    var processor = scope.ServiceProvider.GetRequiredService<ICleanPlaylistJobProcessor>();
    await processor.ProcessJobAsync(jobId, JobCancellationToken.Null);
  }

  private void SeedSupabaseAuthSchemaStub()
  {
    using var conn = new Npgsql.NpgsqlConnection(_postgresContainer.GetConnectionString());
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
      CREATE SCHEMA IF NOT EXISTS auth;
      CREATE TABLE IF NOT EXISTS auth.users (
        id uuid PRIMARY KEY,
        email text,
        raw_user_meta_data jsonb
      );
      """;
    cmd.ExecuteNonQuery();
  }

  private static SpotifyTrack MakeSpotifyTrack(string id, string name, bool isExplicit, string? isrc = null) => new()
  {
    Id = id,
    Name = name,
    Explicit = isExplicit,
    Artists = new[] { new SpotifyArtist { Id = "a1", Name = "Artist" } },
    Album = new SpotifyAlbum { Id = "al", Name = "Album" },
    Uri = $"spotify:track:{id}",
    ExternalIds = isrc is null ? null : new SpotifyExternalIds { Isrc = isrc }
  };

  private static AppleCatalogSong MakeCatalogSong(string id, string name, string? contentRating, string? isrc = null) => new()
  {
    Id = id,
    Attributes = new AppleCatalogSongAttributes
    {
      Name = name,
      ArtistName = "Artist",
      AlbumName = "Album",
      ContentRating = contentRating,
      Isrc = isrc
    }
  };

  private static AppleLibrarySong MakeLibrarySong(string id, string name, string? catalogId = null, string? contentRating = null) => new()
  {
    Id = id,
    Attributes = new AppleLibrarySongAttributes
    {
      Name = name,
      ArtistName = "Artist",
      AlbumName = "Album",
      ContentRating = contentRating,
      PlayParams = catalogId is null ? null : new ApplePlayParams { Id = id, CatalogId = catalogId }
    }
  };

  // Canned ISpotifyService stand-in at the HTTP boundary; records observed calls.
  private sealed class FakeSpotifyService : ISpotifyService
  {
    public Dictionary<string, IEnumerable<SpotifyTrack>> SourcePlaylistTracks { get; } = new();
    public Dictionary<string, SpotifyTrack?> CleanVersionsBySourceId { get; } = new();
    public Dictionary<int, IEnumerable<PlaylistDto>> UserPlaylists { get; } = new();
    public Dictionary<int, SpotifyUserProfile> UserProfile { get; } = new();
    public Dictionary<string, SpotifyTrack> TracksByIsrc { get; } = new();
    public string CreatedPlaylistId { get; set; } = "sp-target";

    public List<AddTracksInvocation> AddTracksInvocations { get; } = new();

    public Task<SpotifyUserProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken = default) =>
      Task.FromResult(UserProfile[userId]);

    public Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken = default) =>
      Task.FromResult(UserPlaylists.TryGetValue(userId, out var playlists) ? playlists : Array.Empty<PlaylistDto>());

    public Task<IEnumerable<SpotifyTrack>> GetPlaylistTracksAsync(int userId, string playlistId, CancellationToken cancellationToken = default) =>
      Task.FromResult(SourcePlaylistTracks[playlistId]);

    public Task<SpotifyPlaylist> CreatePlaylistAsync(int userId, string name, string? description = null, CancellationToken cancellationToken = default) =>
      Task.FromResult(new SpotifyPlaylist
      {
        Id = CreatedPlaylistId,
        Name = name,
        Description = description,
        Tracks = new SpotifyPlaylistTracksRef { Total = 0, Href = "href" },
        Owner = new SpotifyUser { Id = "sp-owner", DisplayName = "Owner" }
      });

    public Task AddTracksToPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackUris, CancellationToken cancellationToken = default)
    {
      AddTracksInvocations.Add(new AddTracksInvocation(userId, playlistId, trackUris.ToArray()));
      return Task.CompletedTask;
    }

    public Task RemoveTracksFromPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackUris, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task<SpotifyTrack?> FindCleanVersionAsync(int userId, SpotifyTrack explicitTrack, CancellationToken cancellationToken = default)
    {
      if (!explicitTrack.Explicit) return Task.FromResult<SpotifyTrack?>(explicitTrack);
      return Task.FromResult(CleanVersionsBySourceId.TryGetValue(explicitTrack.Id, out var clean) ? clean : null);
    }

    public Task<IReadOnlyList<SpotifyTrack>> SearchTracksAsync(int userId, string query, int limit, CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<SpotifyTrack>>(Array.Empty<SpotifyTrack>());

    public Task<SpotifyTrack?> GetTrackByIsrcAsync(int userId, string isrc, CancellationToken cancellationToken = default) =>
      Task.FromResult(TracksByIsrc.TryGetValue(isrc, out var track) ? track : null);
  }

  private sealed record AddTracksInvocation(int UserId, string PlaylistId, string[] TrackUris);

  // Canned IAppleMusicService stand-in at the HTTP boundary; records observed calls.
  private sealed class FakeAppleMusicService : IAppleMusicService
  {
    public Dictionary<int, List<AppleLibraryPlaylist>> UserPlaylists { get; } = new();
    public Dictionary<string, List<AppleLibrarySong>> PlaylistTracks { get; } = new();
    public List<AppleCatalogSong> Catalog { get; } = new();
    public Dictionary<string, List<AppleCatalogSong>> SearchResultsByTerm { get; } = new();
    public string CreatedPlaylistId { get; set; } = "p.target";

    public List<AppleAddTracksInvocation> AddTracksInvocations { get; } = new();
    public List<(string Name, string? Description)> CreatedPlaylists { get; } = new();

    public Task<string> GetUserStorefrontAsync(int userId, CancellationToken cancellationToken = default) =>
      Task.FromResult("us");

    public Task<IEnumerable<AppleLibraryPlaylist>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken = default) =>
      Task.FromResult<IEnumerable<AppleLibraryPlaylist>>(
        UserPlaylists.TryGetValue(userId, out var playlists) ? playlists : new List<AppleLibraryPlaylist>());

    public Task<IEnumerable<AppleLibrarySong>> GetPlaylistTracksAsync(int userId, string playlistId, CancellationToken cancellationToken = default) =>
      Task.FromResult<IEnumerable<AppleLibrarySong>>(PlaylistTracks[playlistId]);

    public Task<IReadOnlyList<AppleCatalogSong>> GetCatalogSongsByIsrcAsync(int userId, IReadOnlyCollection<string> isrcs, CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<AppleCatalogSong>>(
        Catalog.Where(c => c.Attributes.Isrc != null && isrcs.Contains(c.Attributes.Isrc, StringComparer.OrdinalIgnoreCase)).ToList());

    public Task<IReadOnlyList<AppleCatalogSong>> GetCatalogSongsByIdsAsync(int userId, IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<AppleCatalogSong>>(
        Catalog.Where(c => ids.Contains(c.Id, StringComparer.Ordinal)).ToList());

    public Task<IReadOnlyList<AppleCatalogSong>> SearchCatalogSongsAsync(int userId, string term, int limit, CancellationToken cancellationToken = default) =>
      Task.FromResult<IReadOnlyList<AppleCatalogSong>>(
        SearchResultsByTerm.TryGetValue(term, out var results) ? results : new List<AppleCatalogSong>());

    public Task<AppleLibraryPlaylist> CreateLibraryPlaylistAsync(int userId, string name, string? description, CancellationToken cancellationToken = default)
    {
      CreatedPlaylists.Add((name, description));
      return Task.FromResult(new AppleLibraryPlaylist
      {
        Id = CreatedPlaylistId,
        Attributes = new AppleLibraryPlaylistAttributes { Name = name, CanEdit = true }
      });
    }

    public Task AddTracksToLibraryPlaylistAsync(int userId, string playlistId, IEnumerable<string> catalogSongIds, CancellationToken cancellationToken = default)
    {
      AddTracksInvocations.Add(new AppleAddTracksInvocation(userId, playlistId, catalogSongIds.ToArray()));
      return Task.CompletedTask;
    }
  }

  private sealed record AppleAddTracksInvocation(int UserId, string PlaylistId, string[] SongIds);

  private sealed class FakeProgressBroadcast : IProgressBroadcastService
  {
    public Task BroadcastProgressUpdate(int jobId, RadioWash.Api.Models.ProgressUpdate update) =>
      Task.CompletedTask;
    public Task BroadcastJobCompleted(int jobId, string? message = null) => Task.CompletedTask;
    public Task BroadcastJobFailed(int jobId, string error) => Task.CompletedTask;
  }

  private sealed class NoopJobOrchestrator : IJobOrchestrator
  {
    public Task<string> EnqueueJobAsync(int jobId) => Task.FromResult($"fake-hangfire-id-{jobId}");
    public Task CancelJobAsync(string hangfireJobId) => Task.CompletedTask;
  }
}
