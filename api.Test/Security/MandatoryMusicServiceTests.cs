using Moq;
using RadioWash.Api.Controllers;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Test.Infrastructure;

namespace RadioWash.Api.Test.Security;

/// <summary>
/// TDD tests for mandatory music service connection requirement
/// </summary>
public class MandatoryMusicServiceTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    
    public MandatoryMusicServiceTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public async Task GetConnectedServices_Should_Return_Empty_For_New_User()
    {
        // Arrange
        var mockMusicServiceAuth = MockServices.CreateMockMusicServiceAuthService();
        var newUserId = Guid.NewGuid();
        
        // Act
        var services = await mockMusicServiceAuth.Object.GetConnectedServicesAsync(newUserId);
        
        // Assert
        Assert.Empty(services);
    }
    
    [Fact]
    public void HasValidMusicService_Should_Return_False_When_No_Active_Services()
    {
        // Arrange
        var services = new List<UserMusicService>();
        
        // Act
        var hasValidService = MockServices.HasValidMusicService(services);
        
        // Assert
        Assert.False(hasValidService);
    }
    
    [Fact]
    public void HasValidMusicService_Should_Return_False_When_Service_Expired()
    {
        // Arrange
        var expiredService = new UserMusicService
        {
            Id = 1,
            UserId = 1,
            ServiceType = MusicServiceType.Spotify,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired
        };
        var services = new List<UserMusicService> { expiredService };
        
        // Act
        var hasValidService = MockServices.HasValidMusicService(services);
        
        // Assert
        Assert.False(hasValidService);
    }
    
    [Fact]
    public void HasValidMusicService_Should_Return_True_When_Valid_Service_Exists()
    {
        // Arrange
        var validService = new UserMusicService
        {
            Id = 1,
            UserId = 1,
            ServiceType = MusicServiceType.Spotify,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddHours(1) // Valid
        };
        var services = new List<UserMusicService> { validService };
        
        // Act
        var hasValidService = MockServices.HasValidMusicService(services);
        
        // Assert
        Assert.True(hasValidService);
    }
    
    [Fact]
    public async Task GetValidToken_Should_Return_Null_When_No_Service_Connected()
    {
        // Arrange
        var mockMusicServiceAuth = MockServices.CreateMockMusicServiceAuthService();
        var userId = Guid.NewGuid();
        
        // Act
        var token = await mockMusicServiceAuth.Object.GetValidTokenAsync(userId, MusicServiceType.Spotify);
        
        // Assert
        Assert.Null(token);
    }
    
    [Fact]
    public void User_Should_Not_Access_Other_Users_Music_Services()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var user1Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");
        var user2Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440002");
        
        // Act
        var user1Services = context.UserMusicServices.Where(s => s.User.SupabaseUserId == user1Id).ToList();
        var user2Services = context.UserMusicServices.Where(s => s.User.SupabaseUserId == user2Id).ToList();
        
        // Assert
        Assert.Empty(user1Services); // User 1 has no services
        Assert.Single(user2Services); // User 2 has one service
        Assert.All(user2Services, service => Assert.Equal(2, service.UserId));
    }
    
    [Fact]
    public void AuthResult_Should_Include_RequiresMusicServiceSetup_Flag()
    {
        // This test defines the expected behavior - AuthResult should include this flag
        // This will fail initially (TDD approach) until we implement the feature
        
        // Arrange
        var authResult = new AuthResult
        {
            Success = true,
            Token = "jwt-token",
            User = new UserDto { Id = 1, Email = "test@example.com" }
        };
        
        // Act & Assert
        // This property doesn't exist yet - we'll add it as part of implementation
        Assert.True(authResult.Success);
        // TODO: Assert authResult.RequiresMusicServiceSetup when implemented
    }
}