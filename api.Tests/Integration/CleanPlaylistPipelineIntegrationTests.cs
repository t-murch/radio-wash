using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Patterns;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Tests.Integration.TestHelpers;

namespace RadioWash.Api.Tests.Integration;

/// <summary>
/// End-to-end integration test for the clean-playlist pipeline. Exercises the full service
/// layer against a real PostgreSQL database (via Testcontainers) with a canned
/// <see cref="ISpotifyService"/> stub at the HTTP boundary — so every EF Core save, every
/// repository call, every transaction, and every concrete processor/cleaner path is real
/// code running against real state. The assertions cover the three things that matter:
/// the job moves Pending -> Processing -> Completed, the TrackMapping rows persist
/// correctly, and the expected track-ID list is handed to the Spotify "add tracks" call.
///
/// This is the critical-path safety net: any regression in the Dashboard -> processor ->
/// Spotify pipeline surfaces here even if individual unit tests drift.
/// </summary>
public class CleanPlaylistPipelineIntegrationTests : PostgreSqlIntegrationTestBase
{
  private readonly FakeSpotifyService _fakeSpotify;

  public CleanPlaylistPipelineIntegrationTests()
  {
    _fakeSpotify = _serviceProvider.GetRequiredService<FakeSpotifyService>();
  }

  // One of the existing migrations creates a trigger against auth.users (the Supabase-managed
  // schema). Vanilla Postgres in Testcontainers doesn't have it, so we stub it in before
  // migrations run. ConfigureServices is the only subclass hook that runs before the base's
  // Database.Migrate() call, so we create the stub schema here by opening a direct Npgsql
  // connection to the already-running container.
  protected override void ConfigureServices(IServiceCollection services)
  {
    base.ConfigureServices(services);
    SeedSupabaseAuthSchemaStub();

    // Register a single fake Spotify-service stand-in — it records observed calls and
    // returns canned data so the processor and cleaner run against a realistic Spotify API
    // shape without any network traffic.
    services.AddSingleton<FakeSpotifyService>();
    services.AddSingleton<ISpotifyService>(sp => sp.GetRequiredService<FakeSpotifyService>());

    // Wire the full service graph the processor consumes. Mirrors Program.cs registrations
    // but kept in this fixture so we don't pull in the WebApplicationFactory (which would
    // require booting auth + Hangfire + Stripe config).
    services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
    services.AddScoped<IUserMusicTokenRepository, UserMusicTokenRepository>();
    services.AddScoped<IUserRepository, UserRepository>();
    services.AddScoped<ICleanPlaylistJobRepository, CleanPlaylistJobRepository>();
    services.AddScoped<ITrackMappingRepository, TrackMappingRepository>();
    // EntityFrameworkUnitOfWork aggregates every repository, not just the ones the
    // clean-playlist pipeline touches. Register the subscription-side repos too so DI can
    // resolve the unit-of-work; none of their behavior is exercised by the test.
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

    services.AddScoped<IProgressTracker, SmartProgressTracker>();
    services.AddSingleton(new BatchConfiguration());
    services.AddSingleton<IProgressBroadcastService, FakeProgressBroadcast>();

    services.AddScoped<IPlaylistCleanerFactory, PlaylistCleanerFactory>();
    services.AddScoped<IMusicServiceFactory, MusicServiceFactory>();
    services.AddScoped<ICleanPlaylistJobProcessor, CleanPlaylistJobProcessor>();

    // Fake data-protection-backed encryption needs a data protection provider. Add the
    // in-memory default to satisfy the dependency chain.
    services.AddDataProtection();

    // Config stub — TokenEncryptionService may read from IConfiguration for key material.
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
  }

  [Fact]
  public async Task ProcessJob_HappyPath_PersistsTrackMappingsAndAddsCleanTracksToSpotify()
  {
    // Arrange — seed a user with a valid Spotify token, and stage the fake Spotify service
    // with a source playlist containing two explicit tracks and one clean one.
    var user = new User
    {
      SupabaseId = Guid.NewGuid().ToString(),
      DisplayName = "Integration Test User",
      Email = "integration@example.com",
      PrimaryProvider = "spotify",
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _dbContext.Users.Add(user);
    await _dbContext.SaveChangesAsync();

    var encryption = _serviceProvider.GetRequiredService<ITokenEncryptionService>();
    var token = new UserMusicToken
    {
      UserId = user.Id,
      Provider = "spotify",
      EncryptedAccessToken = encryption.EncryptToken("fake-access-token"),
      EncryptedRefreshToken = encryption.EncryptToken("fake-refresh-token"),
      ExpiresAt = DateTime.UtcNow.AddHours(1),
      Scopes = "[\"playlist-modify-private\"]",
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _dbContext.UserMusicTokens.Add(token);
    await _dbContext.SaveChangesAsync();

    var explicitWithMatch = MakeTrack("e1", "Explicit Hit", isExplicit: true);
    var explicitNoMatch = MakeTrack("e2", "Unreleased Mix", isExplicit: true);
    var alreadyClean = MakeTrack("c1", "Squeaky Clean", isExplicit: false);

    _fakeSpotify.SourcePlaylistTracks["source-pl"] = new[] { explicitWithMatch, explicitNoMatch, alreadyClean };
    _fakeSpotify.CleanVersionsBySourceId["e1"] = MakeTrack("e1-clean", "Explicit Hit", isExplicit: false);
    // e2 has no clean version — FindCleanVersion will return null for it
    _fakeSpotify.UserPlaylists[user.Id] = new[]
    {
      new PlaylistDto
      {
        Id = "source-pl",
        Name = "Source Playlist",
        Description = null,
        ImageUrl = null,
        TrackCount = 3,
        OwnerId = "sp-owner",
        OwnerName = "Owner"
      }
    };
    _fakeSpotify.UserProfile[user.Id] = new SpotifyUserProfile
    {
      Id = "sp-owner",
      DisplayName = "Owner",
      Email = "owner@example.com"
    };
    _fakeSpotify.CreatedPlaylistId = "target-pl";

    // Act — create the job and run the processor directly (bypassing Hangfire scheduling).
    // Use separate DI scopes for the two phases so each gets its own DbContext, matching
    // how the Hangfire worker would resolve dependencies in production.
    CleanPlaylistJobDto dto;
    using (var createScope = _serviceProvider.CreateScope())
    {
      var cleanPlaylistService = CreateCleanPlaylistService(createScope.ServiceProvider);
      dto = await cleanPlaylistService.CreateJobAsync(
        user.Id,
        new CreateCleanPlaylistJobDto { SourcePlaylistId = "source-pl" });
    }

    using (var processScope = _serviceProvider.CreateScope())
    {
      var processor = processScope.ServiceProvider.GetRequiredService<ICleanPlaylistJobProcessor>();
      await processor.ProcessJobAsync(dto.Id, JobCancellationToken.Null);
    }

    // Assert — read through a fresh scope so we see committed data, not any stale cached
    // state from the phase-scoped DbContexts above.
    using var assertScope = _serviceProvider.CreateScope();
    var assertDb = assertScope.ServiceProvider.GetRequiredService<RadioWashDbContext>();

    var persistedJob = await assertDb.CleanPlaylistJobs
      .FindAsync(dto.Id) ?? throw new Xunit.Sdk.XunitException("Job disappeared");
    Assert.True(
      persistedJob.Status == "Completed",
      $"Expected Completed, got {persistedJob.Status}. ErrorMessage: {persistedJob.ErrorMessage}");
    Assert.Equal("target-pl", persistedJob.TargetPlaylistId);
    Assert.Equal(3, persistedJob.ProcessedTracks);
    Assert.Equal(2, persistedJob.MatchedTracks); // e1 matched + c1 passed through

    // Assert — track mappings
    var mappings = assertDb.TrackMappings
      .Where(m => m.JobId == persistedJob.Id)
      .OrderBy(m => m.Id)
      .ToList();
    Assert.Equal(3, mappings.Count);
    Assert.True(mappings.Single(m => m.SourceTrackId == "e1").HasCleanMatch);
    Assert.False(mappings.Single(m => m.SourceTrackId == "e2").HasCleanMatch);
    Assert.True(mappings.Single(m => m.SourceTrackId == "c1").HasCleanMatch);

    // Assert — Spotify received the expected track list (raw IDs come out as spotify:track:<id>
    // URIs by the time they reach the ISpotifyService fake).
    Assert.Single(_fakeSpotify.AddTracksInvocations);
    var invocation = _fakeSpotify.AddTracksInvocations.Single();
    Assert.Equal(user.Id, invocation.UserId);
    Assert.Equal("target-pl", invocation.PlaylistId);
    Assert.Equal(
      new[] { "spotify:track:e1-clean", "spotify:track:c1" },
      invocation.TrackUris);
  }

  private void SeedSupabaseAuthSchemaStub()
  {
    // The CreateAuthUserTrigger migration targets Supabase's auth.users table. Stub it with
    // the minimal columns the trigger function reads so Database.Migrate() succeeds.
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

  // Construct CleanPlaylistService manually to avoid registering it in ConfigureServices
  // (which would require registering IJobOrchestrator, and by extension IBackgroundJobClient,
  // which would drag Hangfire registrations into this fixture).
  private CleanPlaylistService CreateCleanPlaylistService(IServiceProvider sp) => new(
    sp.GetRequiredService<IUnitOfWork>(),
    sp.GetRequiredService<IMusicServiceFactory>(),
    sp.GetRequiredService<IMusicTokenService>(),
    new NoopJobOrchestrator(),
    sp.GetRequiredService<ILogger<CleanPlaylistService>>());

  private static SpotifyTrack MakeTrack(string id, string name, bool isExplicit) => new()
  {
    Id = id,
    Name = name,
    Explicit = isExplicit,
    Artists = new[] { new SpotifyArtist { Id = "a1", Name = "Artist" } },
    Album = new SpotifyAlbum { Id = "al", Name = "Album" },
    Uri = $"spotify:track:{id}"
  };

  // A fake Spotify service that records every call and returns canned responses. Playing the
  // role of the HTTP boundary (ISpotifyService is the last hop before the network) so the
  // rest of the pipeline — including the real SpotifyMusicService adapter and its
  // spotify:track:<id> URI formatting — runs against real code.
  private sealed class FakeSpotifyService : ISpotifyService
  {
    public Dictionary<string, IEnumerable<SpotifyTrack>> SourcePlaylistTracks { get; } = new();
    public Dictionary<string, SpotifyTrack?> CleanVersionsBySourceId { get; } = new();
    public Dictionary<int, IEnumerable<PlaylistDto>> UserPlaylists { get; } = new();
    public Dictionary<int, SpotifyUserProfile> UserProfile { get; } = new();
    public string CreatedPlaylistId { get; set; } = "target-pl";

    public List<AddTracksInvocation> AddTracksInvocations { get; } = new();

    public Task<SpotifyUserProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken = default) =>
      Task.FromResult(UserProfile[userId]);

    public Task<IEnumerable<PlaylistDto>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken = default) =>
      Task.FromResult(UserPlaylists[userId]);

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
  }

  private sealed record AddTracksInvocation(int UserId, string PlaylistId, string[] TrackUris);

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
