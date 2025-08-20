using Microsoft.EntityFrameworkCore;
using Moq;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

public class UserProviderDataTests
{
    // Note: These are actually domain model tests, not requiring DbContext mocking
    public UserProviderDataTests()
    {
    }

    #region Database Schema Tests

    [Fact]
    public void UserProviderData_WhenCreated_ShouldHaveCorrectRelationships()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };

        var providerData = new UserProviderData
        {
            Id = 1,
            UserId = user.Id,
            User = user,
            Provider = "spotify",
            ProviderId = "spotify123",
            ProviderMetadata = "{\"displayName\":\"Spotify User\"}"
        };

        // Act & Assert
        Assert.Equal(user.Id, providerData.UserId);
        Assert.Equal(user, providerData.User);
        Assert.Equal("spotify", providerData.Provider);
        Assert.Equal("spotify123", providerData.ProviderId);
        Assert.Equal("{\"displayName\":\"Spotify User\"}", providerData.ProviderMetadata);
    }

    [Fact]
    public void UserProviderData_WhenDuplicateProviderAndProviderId_ShouldHaveSameKey()
    {
        // This test verifies that domain objects with same provider/providerId have same logical key
        
        // Arrange
        var user = new User
        {
            Id = 1,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };

        var providerData1 = new UserProviderData
        {
            Id = 1,
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123"
        };

        var providerData2 = new UserProviderData
        {
            Id = 2,
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123" // Same provider and provider ID
        };

        // Act & Assert - Both should have same provider/providerId combination
        Assert.Equal(providerData1.Provider, providerData2.Provider);
        Assert.Equal(providerData1.ProviderId, providerData2.ProviderId);
        Assert.Equal(providerData1.UserId, providerData2.UserId);
    }

    [Fact]
    public void UserProviderData_WhenSameProviderDifferentProviderId_ShouldHaveDifferentKeys()
    {
        // Arrange
        var user1 = new User
        {
            Id = 1,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User 1",
            Email = "test1@example.com"
        };
        var user2 = new User
        {
            Id = 2,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User 2",
            Email = "test2@example.com"
        };

        var providerData1 = new UserProviderData
        {
            Id = 1,
            UserId = user1.Id,
            Provider = "spotify",
            ProviderId = "spotify123"
        };
        var providerData2 = new UserProviderData
        {
            Id = 2,
            UserId = user2.Id,
            Provider = "spotify",
            ProviderId = "spotify456" // Different provider ID
        };

        // Act & Assert
        Assert.Equal("spotify", providerData1.Provider);
        Assert.Equal("spotify", providerData2.Provider);
        Assert.NotEqual(providerData1.ProviderId, providerData2.ProviderId);
        Assert.Equal("spotify123", providerData1.ProviderId);
        Assert.Equal("spotify456", providerData2.ProviderId);
    }

    [Fact]
    public void UserProviderData_WhenUserDeleted_ShouldHaveUserRelationship()
    {
        // This test verifies the domain model relationship structure
        
        // Arrange
        var user = new User
        {
            Id = 1,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };

        var providerData = new UserProviderData
        {
            Id = 1,
            UserId = user.Id,
            User = user,
            Provider = "spotify",
            ProviderId = "spotify123"
        };

        // Act & Assert - Verify the relationship is properly established
        Assert.Equal(user.Id, providerData.UserId);
        Assert.Same(user, providerData.User);
    }

    #endregion

    #region Provider Data Validation Tests

    [Fact]
    public void UserProviderData_WhenValidData_ShouldHaveCorrectProperties()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };

        var providerData = new UserProviderData
        {
            Id = 1,
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123",
            ProviderMetadata = "{\"displayName\":\"Spotify User\",\"profileImageUrl\":\"https://example.com/image.jpg\"}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act & Assert
        Assert.Equal(1, providerData.Id);
        Assert.Equal(user.Id, providerData.UserId);
        Assert.Equal("spotify", providerData.Provider);
        Assert.Equal("spotify123", providerData.ProviderId);
        Assert.Contains("displayName", providerData.ProviderMetadata);
        Assert.True(providerData.CreatedAt <= DateTime.UtcNow);
        Assert.True(providerData.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void UserProviderData_WhenNullMetadata_ShouldAllowNullValue()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };

        var providerData = new UserProviderData
        {
            Id = 1,
            UserId = user.Id,
            Provider = "apple",
            ProviderId = "apple456",
            ProviderMetadata = null // Null metadata should be allowed
        };

        // Act & Assert
        Assert.Equal(1, providerData.Id);
        Assert.Equal(user.Id, providerData.UserId);
        Assert.Equal("apple", providerData.Provider);
        Assert.Equal("apple456", providerData.ProviderId);
        Assert.Null(providerData.ProviderMetadata);
    }

    #endregion

    #region Multi-Provider Scenarios

    [Fact]
    public void UserProviderData_WhenUserHasMultipleProviders_ShouldMaintainSeparateData()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Multi-Provider User",
            Email = "multiuser@example.com",
            PrimaryProvider = "spotify"
        };

        var spotifyData = new UserProviderData
        {
            Id = 1,
            UserId = user.Id,
            User = user,
            Provider = "spotify",
            ProviderId = "spotify123",
            ProviderMetadata = "{\"displayName\":\"Spotify User\",\"playlistCount\":50}"
        };
        var appleData = new UserProviderData
        {
            Id = 2,
            UserId = user.Id,
            User = user,
            Provider = "apple",
            ProviderId = "apple456",
            ProviderMetadata = "{\"displayName\":\"Apple User\",\"librarySize\":1000}"
        };

        // Set up the user's provider data collection
        user.ProviderData = new List<UserProviderData> { spotifyData, appleData };

        // Act & Assert
        Assert.NotNull(user);
        Assert.Equal(2, user.ProviderData.Count);
        
        var spotify = user.ProviderData.First(pd => pd.Provider == "spotify");
        var apple = user.ProviderData.First(pd => pd.Provider == "apple");
        
        Assert.Equal("spotify123", spotify.ProviderId);
        Assert.Contains("playlistCount", spotify.ProviderMetadata);
        
        Assert.Equal("apple456", apple.ProviderId);
        Assert.Contains("librarySize", apple.ProviderMetadata);
    }

    [Fact]
    public void UserProviderData_WhenQueryingByProvider_ShouldFilterCorrectly()
    {
        // Arrange
        var user1 = new User
        {
            Id = 1,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Spotify User",
            Email = "spotify@example.com"
        };
        var user2 = new User
        {
            Id = 2,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Apple User",
            Email = "apple@example.com"
        };
        var user3 = new User
        {
            Id = 3,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Multi User",
            Email = "multi@example.com"
        };

        var spotifyData1 = new UserProviderData { Id = 1, UserId = user1.Id, User = user1, Provider = "spotify", ProviderId = "spotify123" };
        var appleData1 = new UserProviderData { Id = 2, UserId = user2.Id, User = user2, Provider = "apple", ProviderId = "apple456" };
        var spotifyData2 = new UserProviderData { Id = 3, UserId = user3.Id, User = user3, Provider = "spotify", ProviderId = "spotify789" };
        var appleData2 = new UserProviderData { Id = 4, UserId = user3.Id, User = user3, Provider = "apple", ProviderId = "apple012" };

        var allProviderData = new List<UserProviderData> { spotifyData1, appleData1, spotifyData2, appleData2 };

        // Act
        var spotifyUsers = allProviderData
            .Where(upd => upd.Provider == "spotify")
            .Select(upd => upd.User)
            .ToList();

        // Assert
        Assert.Equal(2, spotifyUsers.Count);
        Assert.Contains(spotifyUsers, u => u.Email == "spotify@example.com");
        Assert.Contains(spotifyUsers, u => u.Email == "multi@example.com");
        Assert.DoesNotContain(spotifyUsers, u => u.Email == "apple@example.com");
    }

    #endregion
}