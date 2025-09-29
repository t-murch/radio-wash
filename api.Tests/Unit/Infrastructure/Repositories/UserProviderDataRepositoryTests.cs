using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Unit.Infrastructure;

namespace RadioWash.Api.Tests.Unit.Infrastructure.Repositories;

/// <summary>
/// Unit tests for UserProviderDataRepository
/// Tests provider data management, lookups, and CRUD operations
/// </summary>
public class UserProviderDataRepositoryTests : RepositoryTestBase
{
    private readonly UserProviderDataRepository _providerDataRepository;

    public UserProviderDataRepositoryTests()
    {
        _providerDataRepository = new UserProviderDataRepository(_context);
    }

    [Fact]
    public async Task GetByProviderAsync_WithExistingProviderData_ReturnsProviderDataWithUser()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(
            user.Id, 
            "spotify", 
            "spotify_unique_123", 
            "{\"name\": \"Test User\"}");
        await SeedAsync(providerData);

        // Act
        var result = await _providerDataRepository.GetByProviderAsync("spotify", "spotify_unique_123");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("spotify", result.Provider);
        Assert.Equal("spotify_unique_123", result.ProviderId);
        Assert.Equal("{\"name\": \"Test User\"}", result.ProviderMetadata);
        
        // Verify User is included
        Assert.NotNull(result.User);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal(user.SupabaseId, result.User.SupabaseId);
    }

    [Fact]
    public async Task GetByProviderAsync_WithNonExistentProvider_ReturnsNull()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
        await SeedAsync(providerData);

        // Act
        var result = await _providerDataRepository.GetByProviderAsync("google", "google_123");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByProviderAsync_WithNonExistentProviderId_ReturnsNull()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
        await SeedAsync(providerData);

        // Act
        var result = await _providerDataRepository.GetByProviderAsync("spotify", "nonexistent_id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByUserAndProviderAsync_WithExistingData_ReturnsProviderData()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(
            user.Id, 
            "google", 
            "google_456", 
            "{\"email\": \"test@gmail.com\"}");
        await SeedAsync(providerData);

        // Act
        var result = await _providerDataRepository.GetByUserAndProviderAsync(user.Id, "google");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal("google", result.Provider);
        Assert.Equal("google_456", result.ProviderId);
        Assert.Equal("{\"email\": \"test@gmail.com\"}", result.ProviderMetadata);
    }

    [Fact]
    public async Task GetByUserAndProviderAsync_WithDifferentUser_ReturnsNull()
    {
        // Arrange
        var user1 = CreateTestUser(supabaseId: "sb_user1");
        var user2 = CreateTestUser(supabaseId: "sb_user2");
        await SeedAsync(user1, user2);

        var providerData = CreateTestUserProviderData(user1.Id, "spotify", "spotify_123");
        await SeedAsync(providerData);

        // Act
        var result = await _providerDataRepository.GetByUserAndProviderAsync(user2.Id, "spotify");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByUserAndProviderAsync_WithDifferentProvider_ReturnsNull()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
        await SeedAsync(providerData);

        // Act
        var result = await _providerDataRepository.GetByUserAndProviderAsync(user.Id, "google");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByUserIdAsync_WithMultipleProviders_ReturnsAllProviderData()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var spotifyData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
        var googleData = CreateTestUserProviderData(user.Id, "google", "google_456");
        var appleData = CreateTestUserProviderData(user.Id, "apple", "apple_789");
        await SeedAsync(spotifyData, googleData, appleData);

        // Act
        var result = await _providerDataRepository.GetByUserIdAsync(user.Id);

        // Assert
        var providerDataList = result.ToList();
        Assert.Equal(3, providerDataList.Count);
        
        Assert.Contains(providerDataList, pd => pd.Provider == "spotify" && pd.ProviderId == "spotify_123");
        Assert.Contains(providerDataList, pd => pd.Provider == "google" && pd.ProviderId == "google_456");
        Assert.Contains(providerDataList, pd => pd.Provider == "apple" && pd.ProviderId == "apple_789");
    }

    [Fact]
    public async Task GetByUserIdAsync_WithNoProviders_ReturnsEmptyList()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        // Act
        var result = await _providerDataRepository.GetByUserIdAsync(user.Id);

        // Assert
        var providerDataList = result.ToList();
        Assert.Empty(providerDataList);
    }

    [Fact]
    public async Task GetByUserIdAsync_WithNonExistentUser_ReturnsEmptyList()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
        await SeedAsync(providerData);

        // Act
        var result = await _providerDataRepository.GetByUserIdAsync(999);

        // Assert
        var providerDataList = result.ToList();
        Assert.Empty(providerDataList);
    }

    [Fact]
    public async Task CreateAsync_WithValidProviderData_CreatesSuccessfully()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(
            user.Id, 
            "twitter", 
            "twitter_789", 
            "{\"username\": \"testuser\", \"followers\": 100}");

        // Act
        var result = await _providerDataRepository.CreateAsync(providerData);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal("twitter", result.Provider);
        Assert.Equal("twitter_789", result.ProviderId);
        Assert.Contains("testuser", result.ProviderMetadata!);

        // Verify it was saved to database
        var savedData = await _context.UserProviderData.FindAsync(result.Id);
        Assert.NotNull(savedData);
        Assert.Equal("twitter", savedData.Provider);
    }

    [Fact]
    public async Task UpdateAsync_WithValidProviderData_UpdatesAndTimestamp()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
        await SeedAsync(providerData);

        var originalUpdatedAt = providerData.UpdatedAt;
        
        // Detach to simulate fresh load
        DetachAllEntities();
        
        var dataToUpdate = await _context.UserProviderData.FindAsync(providerData.Id);
        Assert.NotNull(dataToUpdate);
        
        dataToUpdate.ProviderId = "spotify_456_updated";
        dataToUpdate.ProviderMetadata = "{\"updated\": true}";

        // Act
        var result = await _providerDataRepository.UpdateAsync(dataToUpdate);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("spotify_456_updated", result.ProviderId);
        Assert.Equal("{\"updated\": true}", result.ProviderMetadata);
        Assert.True(result.UpdatedAt > originalUpdatedAt);

        // Verify it was updated in database
        DetachAllEntities();
        var updatedData = await _context.UserProviderData.FindAsync(providerData.Id);
        Assert.NotNull(updatedData);
        Assert.Equal("spotify_456_updated", updatedData.ProviderId);
        Assert.Equal("{\"updated\": true}", updatedData.ProviderMetadata);
    }

    [Fact]
    public async Task DeleteAsync_WithValidProviderData_DeletesSuccessfully()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
        await SeedAsync(providerData);

        var dataId = providerData.Id;

        // Act
        await _providerDataRepository.DeleteAsync(providerData);

        // Assert
        var deletedData = await _context.UserProviderData.FindAsync(dataId);
        Assert.Null(deletedData);
    }

    [Fact]
    public async Task SaveChangesAsync_WithPendingChanges_SavesChanges()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
        _context.UserProviderData.Add(providerData);

        // Act
        await _providerDataRepository.SaveChangesAsync();

        // Assert
        Assert.True(providerData.Id > 0);
        
        // Verify it was saved
        var savedData = await _context.UserProviderData.FindAsync(providerData.Id);
        Assert.NotNull(savedData);
        Assert.Equal(providerData.Provider, savedData.Provider);
    }

    [Fact]
    public async Task GetByProviderAsync_WithMultipleUsersForSameProvider_ReturnsCorrectData()
    {
        // Arrange
        var user1 = CreateTestUser(supabaseId: "sb_user1", email: "user1@example.com");
        var user2 = CreateTestUser(supabaseId: "sb_user2", email: "user2@example.com");
        await SeedAsync(user1, user2);

        var provider1Data = CreateTestUserProviderData(user1.Id, "spotify", "spotify_user1");
        var provider2Data = CreateTestUserProviderData(user2.Id, "spotify", "spotify_user2");
        await SeedAsync(provider1Data, provider2Data);

        // Act
        var result1 = await _providerDataRepository.GetByProviderAsync("spotify", "spotify_user1");
        var result2 = await _providerDataRepository.GetByProviderAsync("spotify", "spotify_user2");

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(user1.Id, result1.User.Id);
        Assert.Equal(user2.Id, result2.User.Id);
        Assert.Equal("spotify_user1", result1.ProviderId);
        Assert.Equal("spotify_user2", result2.ProviderId);
    }

    [Fact]
    public async Task CreateAsync_WithMultipleProvidersForSameUser_CreatesAllSuccessfully()
    {
        // Arrange
        var user = CreateTestUser();
        await SeedAsync(user);

        var spotifyData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
        var googleData = CreateTestUserProviderData(user.Id, "google", "google_456");

        // Act
        var result1 = await _providerDataRepository.CreateAsync(spotifyData);
        var result2 = await _providerDataRepository.CreateAsync(googleData);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotEqual(result1.Id, result2.Id);
        Assert.Equal(user.Id, result1.UserId);
        Assert.Equal(user.Id, result2.UserId);
        Assert.Equal("spotify", result1.Provider);
        Assert.Equal("google", result2.Provider);
    }
}