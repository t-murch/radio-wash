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
using RadioWash.Api.Models.Music;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Tests.Integration.TestHelpers;

namespace RadioWash.Api.Tests.Integration;

/// <summary>
/// End-to-end integration test for the clean-playlist pipeline. Exercises the full service
/// layer against a real PostgreSQL database (via Testcontainers) with a canned
/// <see cref="IMusicService"/> stub at the provider boundary — so every EF Core save, every
/// repository call, every transaction, and every concrete processor/cleaner path is real
/// code running against real state. The assertions cover the three things that matter:
/// the job moves Pending -> Processing -> Completed, the TrackMapping rows persist
/// correctly, and the expected track-ID list is handed to the "add tracks" call.
///
/// This is the critical-path safety net: any regression in the Dashboard -> processor ->
/// provider pipeline surfaces here even if individual unit tests drift.
/// </summary>
public class CleanPlaylistPipelineIntegrationTests : PostgreSqlIntegrationTestBase
{
  private readonly FakeMusicService _fakeMusic;

  public CleanPlaylistPipelineIntegrationTests()
  {
    _fakeMusic = _serviceProvider.GetRequiredService<FakeMusicService>();
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

    // Register a single fake music-service stand-in — it records observed calls and returns
    // canned data so the processor and cleaner run against a realistic provider shape
    // without any network traffic.
    services.AddSingleton<FakeMusicService>();

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
    services.AddHttpClient();

    services.AddKeyedSingleton<IMusicService>(
      MusicProviders.AppleMusic,
      (sp, _) => sp.GetRequiredService<FakeMusicService>());

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
  public async Task ProcessJob_HappyPath_PersistsTrackMappingsAndAddsCleanTracksToProvider()
  {
    // Arrange — seed a user with a valid provider token, and stage the fake music service
    // with a source playlist containing two explicit tracks and one clean one.
    var user = new User
    {
      SupabaseId = Guid.NewGuid().ToString(),
      DisplayName = "Integration Test User",
      Email = "integration@example.com",
      PrimaryProvider = MusicProviders.AppleMusic,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _dbContext.Users.Add(user);
    await _dbContext.SaveChangesAsync();

    var encryption = _serviceProvider.GetRequiredService<ITokenEncryptionService>();
    var token = new UserMusicToken
    {
      UserId = user.Id,
      Provider = MusicProviders.AppleMusic,
      EncryptedAccessToken = encryption.EncryptToken("fake-music-user-token"),
      // Apple Music User Tokens have no refresh counterpart.
      EncryptedRefreshToken = null,
      ExpiresAt = DateTime.UtcNow.AddHours(1),
      Scopes = "[]",
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    _dbContext.UserMusicTokens.Add(token);
    await _dbContext.SaveChangesAsync();

    var explicitWithMatch = MakeTrack("e1", "Explicit Hit", isExplicit: true);
    var explicitNoMatch = MakeTrack("e2", "Unreleased Mix", isExplicit: true);
    var alreadyClean = MakeTrack("c1", "Squeaky Clean", isExplicit: false);

    _fakeMusic.SourcePlaylistTracks["source-pl"] = new[] { explicitWithMatch, explicitNoMatch, alreadyClean };
    _fakeMusic.CleanVersionsBySourceId["e1"] = MakeTrack("e1-clean", "Explicit Hit", isExplicit: false);
    // e2 has no clean version — FindCleanVersion will return null for it
    _fakeMusic.UserPlaylists[user.Id] = new[]
    {
      new PlaylistSummary(
        Id: "source-pl",
        Name: "Source Playlist",
        Description: null,
        ImageUrl: null,
        TrackCount: 3,
        OwnerId: "am-owner",
        OwnerName: "Owner")
    };
    _fakeMusic.UserProfile[user.Id] = new MusicUserProfile(
      Id: "am-owner",
      DisplayName: "Owner",
      Email: "owner@example.com");
    _fakeMusic.CreatedPlaylistId = "target-pl";

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

    // Assert — the provider received the expected track list. IMusicService takes raw
    // platform-native IDs; any URI formatting is the adapter's business, below this seam.
    Assert.Single(_fakeMusic.AddTracksInvocations);
    var invocation = _fakeMusic.AddTracksInvocations.Single();
    Assert.Equal(user.Id, invocation.UserId);
    Assert.Equal("target-pl", invocation.PlaylistId);
    Assert.Equal(new[] { "e1-clean", "c1" }, invocation.TrackIds);
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

  private static MusicTrack MakeTrack(string id, string name, bool isExplicit) => new(
    Id: id,
    Name: name,
    IsExplicit: isExplicit,
    Artists: new[] { new MusicArtist("Artist") },
    Isrc: null,
    DurationMs: 200_000,
    AlbumName: "Album");

  // A fake music service that records every call and returns canned responses. It sits at the
  // provider boundary (IMusicService is the seam below which platform-specific HTTP lives), so
  // everything above it — the processor, cleaner, progress tracking, repositories, and every
  // EF Core write — runs as real code against the real database.
  private sealed class FakeMusicService : IMusicService
  {
    public string ProviderName => MusicProviders.AppleMusic;

    public Dictionary<string, IReadOnlyList<MusicTrack>> SourcePlaylistTracks { get; } = new();
    public Dictionary<string, MusicTrack?> CleanVersionsBySourceId { get; } = new();
    public Dictionary<int, IReadOnlyList<PlaylistSummary>> UserPlaylists { get; } = new();
    public Dictionary<int, MusicUserProfile> UserProfile { get; } = new();
    public string CreatedPlaylistId { get; set; } = "target-pl";

    public List<AddTracksInvocation> AddTracksInvocations { get; } = new();

    public Task<MusicUserProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken) =>
      Task.FromResult(UserProfile[userId]);

    public Task<IReadOnlyList<PlaylistSummary>> GetUserPlaylistsAsync(int userId, CancellationToken cancellationToken) =>
      Task.FromResult(UserPlaylists[userId]);

    public Task<IReadOnlyList<MusicTrack>> GetPlaylistTracksAsync(int userId, string playlistId, CancellationToken cancellationToken) =>
      Task.FromResult(SourcePlaylistTracks[playlistId]);

    public Task<PlaylistSummary> CreatePlaylistAsync(int userId, string name, string? description, CancellationToken cancellationToken) =>
      Task.FromResult(new PlaylistSummary(
        Id: CreatedPlaylistId,
        Name: name,
        Description: description,
        ImageUrl: null,
        TrackCount: 0,
        OwnerId: "am-owner",
        OwnerName: "Owner"));

    public Task AddTracksToPlaylistAsync(int userId, string playlistId, IEnumerable<string> trackIds, CancellationToken cancellationToken)
    {
      AddTracksInvocations.Add(new AddTracksInvocation(userId, playlistId, trackIds.ToArray()));
      return Task.CompletedTask;
    }

    public Task<MusicTrack?> FindCleanVersionAsync(int userId, MusicTrack explicitTrack, CancellationToken cancellationToken)
    {
      if (!explicitTrack.IsExplicit) return Task.FromResult<MusicTrack?>(explicitTrack);
      return Task.FromResult(CleanVersionsBySourceId.TryGetValue(explicitTrack.Id, out var clean) ? clean : null);
    }

    public Task<IReadOnlyDictionary<string, MusicTrack>> GetTracksByIsrcAsync(
        int userId, IReadOnlyCollection<string> isrcs, CancellationToken cancellationToken) =>
      Task.FromResult<IReadOnlyDictionary<string, MusicTrack>>(new Dictionary<string, MusicTrack>());

    public Task<IReadOnlyList<MusicTrack>> SearchTracksAsync(int userId, string query, int limit, CancellationToken cancellationToken) =>
      Task.FromResult<IReadOnlyList<MusicTrack>>(Array.Empty<MusicTrack>());
  }

  private sealed record AddTracksInvocation(int UserId, string PlaylistId, string[] TrackIds);

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
