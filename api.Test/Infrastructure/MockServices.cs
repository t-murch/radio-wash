using Moq;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Controllers;

namespace RadioWash.Api.Test.Infrastructure;

public static class MockServices
{
    public static Mock<IAuthService> CreateMockAuthService()
    {
        var mock = new Mock<IAuthService>();
        
        // Default behavior - successful operations
        mock.Setup(x => x.SignUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new AuthResult { Success = true });
            
        mock.Setup(x => x.SignInAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new AuthResult { Success = true });
            
        mock.Setup(x => x.GetUserBySupabaseIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new UserDto 
            { 
                Id = 1, 
                Email = "test@example.com",
                DisplayName = "Test User"
            });
            
        return mock;
    }
    
    public static Mock<IMusicServiceAuthService> CreateMockMusicServiceAuthService()
    {
        var mock = new Mock<IMusicServiceAuthService>();
        
        mock.Setup(x => x.GetConnectedServicesAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new List<UserMusicService>());
            
        mock.Setup(x => x.GetValidTokenAsync(It.IsAny<Guid>(), It.IsAny<MusicServiceType>()))
            .ReturnsAsync((string?)null); // Default: no valid tokens
            
        return mock;
    }
    
    // Helper methods for test scenarios
    public static bool HasValidMusicService(IEnumerable<UserMusicService> services)
    {
        return services.Any(s => s.IsActive && s.ExpiresAt > DateTime.UtcNow);
    }
}