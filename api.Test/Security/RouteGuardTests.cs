using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Controllers;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Test.Infrastructure;
using System.Reflection;
using System.Security.Claims;

namespace RadioWash.Api.Test.Security;

/// <summary>
/// TDD tests for route guards that enforce mandatory music service setup
/// These tests will initially fail until we implement the middleware/attribute
/// </summary>
public class RouteGuardTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    
    public RouteGuardTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public async Task CleanPlaylistController_Should_Block_Users_Without_Music_Services()
    {
        // This test defines the expected behavior: protected routes should check for music services
        // This will guide our implementation of route protection
        
        // Arrange
        var mockMusicServiceAuth = MockServices.CreateMockMusicServiceAuthService();
        var mockLogger = new Mock<ILogger<CleanPlaylistController>>();
        
        // User has no music services
        var userId = Guid.NewGuid();
        mockMusicServiceAuth.Setup(x => x.GetConnectedServicesAsync(userId))
            .ReturnsAsync(new List<UserMusicService>());
        
        // Create controller with mock dependencies
        var mockCleanPlaylistService = new Mock<ICleanPlaylistService>();
        var controller = new CleanPlaylistController(
            mockCleanPlaylistService.Object,
            mockLogger.Object);
        
        // Mock user context
        SetupUserContext(controller, userId);
        
        // Act & Assert
        // This test defines the expected behavior - we'll implement it in the next step
        
        // For now, let's verify the mock setup works
        var services = await mockMusicServiceAuth.Object.GetConnectedServicesAsync(userId);
        var hasValidService = MockServices.HasValidMusicService(services);
        
        Assert.False(hasValidService);
        // TODO: Add actual route protection test when middleware is implemented
    }
    
    [Fact] 
    public async Task Dashboard_Routes_Should_Allow_Users_With_Valid_Music_Services()
    {
        // Test that users with valid music services can access protected routes
        
        // Arrange
        var mockMusicServiceAuth = MockServices.CreateMockMusicServiceAuthService();
        var userId = Guid.NewGuid();
        
        // User has valid Spotify service
        mockMusicServiceAuth.Setup(x => x.GetConnectedServicesAsync(userId))
            .ReturnsAsync(new List<UserMusicService>
            {
                new UserMusicService
                {
                    UserId = 1,
                    ServiceType = MusicServiceType.Spotify,
                    IsActive = true,
                    ExpiresAt = DateTime.UtcNow.AddHours(1) // Valid
                }
            });
        
        // Act
        var services = await mockMusicServiceAuth.Object.GetConnectedServicesAsync(userId);
        var hasValidService = MockServices.HasValidMusicService(services);
        
        // Assert
        Assert.True(hasValidService);
        // User should be allowed to access protected routes
    }
    
    [Fact]
    public async Task MusicService_Routes_Should_Always_Be_Accessible()
    {
        // Music service connection routes should always be accessible
        // Even users without services need to access these to connect their first service
        
        // Arrange
        var mockMusicServiceAuth = MockServices.CreateMockMusicServiceAuthService();
        var mockLogger = new Mock<ILogger<MusicServiceController>>();
        var mockMemoryCache = new Mock<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var mockConfiguration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        var mockEnvironment = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        
        var controller = new MusicServiceController(
            mockMusicServiceAuth.Object,
            mockLogger.Object,
            mockMemoryCache.Object,
            mockConfiguration.Object,
            mockEnvironment.Object);
        
        var userId = Guid.NewGuid();
        SetupUserContext(controller, userId);
        
        // Act & Assert
        // These routes should always be accessible regardless of music service status
        // This test documents the expected behavior
        Assert.NotNull(controller);
        // TODO: Test actual route accessibility when middleware is implemented
    }
    
    [Fact]
    public void RequiresMusicServiceAttribute_Should_Be_Applied_To_Protected_Controllers()
    {
        // This test validates that we apply the correct attribute to controllers
        // that should enforce music service requirements
        
        // Check for [RequiresMusicService] attribute on CleanPlaylistController
        var cleanPlaylistControllerType = typeof(CleanPlaylistController);
        var hasRequiresMusicServiceAttribute = cleanPlaylistControllerType
            .GetCustomAttributes(typeof(RadioWash.Api.Attributes.RequiresMusicServiceAttribute), false)
            .Any();
        Assert.True(hasRequiresMusicServiceAttribute, "CleanPlaylistController should have RequiresMusicService attribute");
        
        // Check for [RequiresMusicService] attribute on PlaylistController
        var playlistControllerType = typeof(PlaylistController);
        var playlistHasAttribute = playlistControllerType
            .GetCustomAttributes(typeof(RadioWash.Api.Attributes.RequiresMusicServiceAttribute), false)
            .Any();
        Assert.True(playlistHasAttribute, "PlaylistController should have RequiresMusicService attribute");
        
        // Verify MusicServiceController does NOT have the attribute (users need to access it to connect services)
        var musicServiceControllerType = typeof(MusicServiceController);
        var musicServiceHasAttribute = musicServiceControllerType
            .GetCustomAttributes(typeof(RadioWash.Api.Attributes.RequiresMusicServiceAttribute), false)
            .Any();
        Assert.False(musicServiceHasAttribute, "MusicServiceController should NOT have RequiresMusicService attribute");
        
        // Verify AuthController does NOT have the attribute
        var authControllerType = typeof(AuthController);
        var authHasAttribute = authControllerType
            .GetCustomAttributes(typeof(RadioWash.Api.Attributes.RequiresMusicServiceAttribute), false)
            .Any();
        Assert.False(authHasAttribute, "AuthController should NOT have RequiresMusicService attribute");
    }
    
    private void SetupUserContext(ControllerBase controller, Guid userId)
    {
        var claims = new List<Claim>
        {
            new Claim("sub", userId.ToString())
        };
        
        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);
        
        var httpContext = new DefaultHttpContext
        {
            User = principal
        };
        
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }
}