using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Controllers;

namespace RadioWash.Api.Test.Infrastructure;

public class TestDatabaseFixture : IDisposable
{
    private readonly DbContextOptions<RadioWashDbContext> _options;
    
    public TestDatabaseFixture()
    {
        _options = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }
    
    public RadioWashDbContext CreateContext()
    {
        var context = new RadioWashDbContext(_options);
        SeedTestData(context);
        return context;
    }
    
    private void SeedTestData(RadioWashDbContext context)
    {
        // Clear existing data
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
        
        // Add test users
        var testUser = new User
        {
            Id = 1,
            SupabaseUserId = Guid.Parse("550e8400-e29b-41d4-a716-446655440001"),
            DisplayName = "Test User",
            Email = "test@example.com",
            CreatedAt = DateTime.UtcNow
        };
        
        var userWithSpotify = new User
        {
            Id = 2,
            SupabaseUserId = Guid.Parse("550e8400-e29b-41d4-a716-446655440002"),
            DisplayName = "User With Spotify",
            Email = "spotify@example.com",
            CreatedAt = DateTime.UtcNow
        };
        
        context.Users.AddRange(testUser, userWithSpotify);
        
        // Add music service for second user
        var spotifyService = new UserMusicService
        {
            Id = 1,
            UserId = 2,
            ServiceType = MusicServiceType.Spotify,
            ServiceUserId = "spotify-user-123",
            AccessToken = "test-access-token",
            RefreshToken = "test-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        
        context.UserMusicServices.Add(spotifyService);
        context.SaveChanges();
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}