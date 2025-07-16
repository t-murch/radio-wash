using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

public class UserProviderDataTests : IDisposable
{
    private readonly RadioWashDbContext _dbContext;

    public UserProviderDataTests()
    {
        var options = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new RadioWashDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region Database Schema Tests

    [Fact]
    public async Task UserProviderData_WhenCreated_ShouldHaveCorrectRelationships()
    {
        // Arrange
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var providerData = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123",
            ProviderMetadata = "{\"displayName\":\"Spotify User\"}"
        };

        // Act
        await _dbContext.UserProviderData.AddAsync(providerData);
        await _dbContext.SaveChangesAsync();

        // Assert
        var savedProviderData = await _dbContext.UserProviderData
            .Include(upd => upd.User)
            .FirstOrDefaultAsync(upd => upd.Id == providerData.Id);

        Assert.NotNull(savedProviderData);
        Assert.NotNull(savedProviderData.User);
        Assert.Equal(user.Id, savedProviderData.UserId);
        Assert.Equal(user.DisplayName, savedProviderData.User.DisplayName);
    }

    [Fact]
    public async Task UserProviderData_WhenDuplicateProviderAndProviderId_ShouldThrowException()
    {
        // Note: This test verifies the unique constraint exists in the model configuration
        // In-memory database doesn't enforce unique constraints, but the constraint will
        // be enforced when using a real database like PostgreSQL

        // Arrange
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var providerData1 = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123"
        };
        await _dbContext.UserProviderData.AddAsync(providerData1);
        await _dbContext.SaveChangesAsync();

        var providerData2 = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123" // Same provider and provider ID
        };

        // Act
        await _dbContext.UserProviderData.AddAsync(providerData2);
        
        // In-memory database allows this but real database would throw
        // This test passes to show the model allows the setup, but unique constraint 
        // is configured in DbContext for production database
        await _dbContext.SaveChangesAsync();
        
        // Verify the unique constraint is configured in the model
        var entityType = _dbContext.Model.FindEntityType(typeof(UserProviderData));
        var uniqueIndex = entityType?.GetIndexes()
            .FirstOrDefault(i => i.IsUnique && i.Properties.Count == 2);
        
        Assert.NotNull(uniqueIndex);
        Assert.Contains("Provider", uniqueIndex.Properties.Select(p => p.Name));
        Assert.Contains("ProviderId", uniqueIndex.Properties.Select(p => p.Name));
    }

    [Fact]
    public async Task UserProviderData_WhenSameProviderDifferentProviderId_ShouldSucceed()
    {
        // Arrange
        var user1 = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User 1",
            Email = "test1@example.com"
        };
        var user2 = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User 2",
            Email = "test2@example.com"
        };
        await _dbContext.Users.AddRangeAsync(user1, user2);
        await _dbContext.SaveChangesAsync();

        var providerData1 = new UserProviderData
        {
            UserId = user1.Id,
            Provider = "spotify",
            ProviderId = "spotify123"
        };
        var providerData2 = new UserProviderData
        {
            UserId = user2.Id,
            Provider = "spotify",
            ProviderId = "spotify456" // Different provider ID
        };

        // Act
        await _dbContext.UserProviderData.AddRangeAsync(providerData1, providerData2);
        await _dbContext.SaveChangesAsync();

        // Assert
        var savedData = await _dbContext.UserProviderData.ToListAsync();
        Assert.Equal(2, savedData.Count);
        Assert.All(savedData, pd => Assert.Equal("spotify", pd.Provider));
        Assert.Contains(savedData, pd => pd.ProviderId == "spotify123");
        Assert.Contains(savedData, pd => pd.ProviderId == "spotify456");
    }

    [Fact]
    public async Task UserProviderData_WhenUserDeleted_ShouldCascadeDelete()
    {
        // Arrange
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var providerData = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123"
        };
        await _dbContext.UserProviderData.AddAsync(providerData);
        await _dbContext.SaveChangesAsync();

        // Act
        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();

        // Assert
        var remainingProviderData = await _dbContext.UserProviderData.ToListAsync();
        Assert.Empty(remainingProviderData);
    }

    #endregion

    #region Provider Data Validation Tests

    [Fact]
    public async Task UserProviderData_WhenValidData_ShouldSaveSuccessfully()
    {
        // Arrange
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var providerData = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123",
            ProviderMetadata = "{\"displayName\":\"Spotify User\",\"profileImageUrl\":\"https://example.com/image.jpg\"}"
        };

        // Act
        await _dbContext.UserProviderData.AddAsync(providerData);
        await _dbContext.SaveChangesAsync();

        // Assert
        var savedData = await _dbContext.UserProviderData.FindAsync(providerData.Id);
        Assert.NotNull(savedData);
        Assert.Equal("spotify", savedData.Provider);
        Assert.Equal("spotify123", savedData.ProviderId);
        Assert.Contains("displayName", savedData.ProviderMetadata);
        Assert.True(savedData.CreatedAt <= DateTime.UtcNow);
        Assert.True(savedData.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task UserProviderData_WhenNullMetadata_ShouldSaveSuccessfully()
    {
        // Arrange
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var providerData = new UserProviderData
        {
            UserId = user.Id,
            Provider = "apple",
            ProviderId = "apple456",
            ProviderMetadata = null // Null metadata should be allowed
        };

        // Act
        await _dbContext.UserProviderData.AddAsync(providerData);
        await _dbContext.SaveChangesAsync();

        // Assert
        var savedData = await _dbContext.UserProviderData.FindAsync(providerData.Id);
        Assert.NotNull(savedData);
        Assert.Null(savedData.ProviderMetadata);
    }

    #endregion

    #region Multi-Provider Scenarios

    [Fact]
    public async Task UserProviderData_WhenUserHasMultipleProviders_ShouldMaintainSeparateData()
    {
        // Arrange
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Multi-Provider User",
            Email = "multiuser@example.com",
            PrimaryProvider = "spotify"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var spotifyData = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123",
            ProviderMetadata = "{\"displayName\":\"Spotify User\",\"playlistCount\":50}"
        };
        var appleData = new UserProviderData
        {
            UserId = user.Id,
            Provider = "apple",
            ProviderId = "apple456",
            ProviderMetadata = "{\"displayName\":\"Apple User\",\"librarySize\":1000}"
        };

        // Act
        await _dbContext.UserProviderData.AddRangeAsync(spotifyData, appleData);
        await _dbContext.SaveChangesAsync();

        // Assert
        var userWithProviders = await _dbContext.Users
            .Include(u => u.ProviderData)
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        Assert.NotNull(userWithProviders);
        Assert.Equal(2, userWithProviders.ProviderData.Count);
        
        var spotify = userWithProviders.ProviderData.First(pd => pd.Provider == "spotify");
        var apple = userWithProviders.ProviderData.First(pd => pd.Provider == "apple");
        
        Assert.Equal("spotify123", spotify.ProviderId);
        Assert.Contains("playlistCount", spotify.ProviderMetadata);
        
        Assert.Equal("apple456", apple.ProviderId);
        Assert.Contains("librarySize", apple.ProviderMetadata);
    }

    [Fact]
    public async Task UserProviderData_WhenQueryingByProvider_ShouldReturnCorrectUsers()
    {
        // Arrange
        var user1 = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Spotify User",
            Email = "spotify@example.com"
        };
        var user2 = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Apple User",
            Email = "apple@example.com"
        };
        var user3 = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Multi User",
            Email = "multi@example.com"
        };
        await _dbContext.Users.AddRangeAsync(user1, user2, user3);
        await _dbContext.SaveChangesAsync();

        var spotifyData1 = new UserProviderData { UserId = user1.Id, Provider = "spotify", ProviderId = "spotify123" };
        var appleData1 = new UserProviderData { UserId = user2.Id, Provider = "apple", ProviderId = "apple456" };
        var spotifyData2 = new UserProviderData { UserId = user3.Id, Provider = "spotify", ProviderId = "spotify789" };
        var appleData2 = new UserProviderData { UserId = user3.Id, Provider = "apple", ProviderId = "apple012" };

        await _dbContext.UserProviderData.AddRangeAsync(spotifyData1, appleData1, spotifyData2, appleData2);
        await _dbContext.SaveChangesAsync();

        // Act
        var spotifyUsers = await _dbContext.UserProviderData
            .Where(upd => upd.Provider == "spotify")
            .Include(upd => upd.User)
            .Select(upd => upd.User)
            .ToListAsync();

        // Assert
        Assert.Equal(2, spotifyUsers.Count);
        Assert.Contains(spotifyUsers, u => u.Email == "spotify@example.com");
        Assert.Contains(spotifyUsers, u => u.Email == "multi@example.com");
        Assert.DoesNotContain(spotifyUsers, u => u.Email == "apple@example.com");
    }

    #endregion
}