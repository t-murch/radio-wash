using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

public class UserServiceTests : IDisposable
{
    private readonly RadioWashDbContext _dbContext;
    private readonly Mock<ILogger<UserService>> _loggerMock;
    private readonly UserService _sut;

    public UserServiceTests()
    {
        var options = new DbContextOptionsBuilder<RadioWashDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new RadioWashDbContext(options);
        _loggerMock = new Mock<ILogger<UserService>>();
        _sut = new UserService(_dbContext, _loggerMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region GetUserBySupabaseIdAsync Tests

    [Fact]
    public async Task GetUserBySupabaseIdAsync_WhenUserExists_ShouldReturnUserDto()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();
        var user = new User
        {
            SupabaseId = supabaseId.ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetUserBySupabaseIdAsync(supabaseId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.SupabaseId, result.SupabaseId);
        Assert.Equal(user.DisplayName, result.DisplayName);
        Assert.Equal(user.Email, result.Email);
        Assert.Equal(user.PrimaryProvider, result.PrimaryProvider);
    }

    [Fact]
    public async Task GetUserBySupabaseIdAsync_WhenUserDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        var nonExistentSupabaseId = Guid.NewGuid();

        // Act
        var result = await _sut.GetUserBySupabaseIdAsync(nonExistentSupabaseId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetUserBySupabaseIdAsync_WhenUserHasProviderData_ShouldIncludeProviderData()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();
        var user = new User
        {
            SupabaseId = supabaseId.ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify"
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
        await _dbContext.UserProviderData.AddAsync(providerData);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetUserBySupabaseIdAsync(supabaseId);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.ProviderData);
        Assert.Equal("spotify", result.ProviderData.First().Provider);
        Assert.Equal("spotify123", result.ProviderData.First().ProviderId);
    }

    #endregion

    #region GetUserByEmailAsync Tests

    [Fact]
    public async Task GetUserByEmailAsync_WhenUserExists_ShouldReturnUserDto()
    {
        // Arrange
        var email = "test@example.com";
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = email,
            PrimaryProvider = "email"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetUserByEmailAsync(email);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(email, result.Email);
    }

    [Fact]
    public async Task GetUserByEmailAsync_WhenUserDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        var nonExistentEmail = "nonexistent@example.com";

        // Act
        var result = await _sut.GetUserByEmailAsync(nonExistentEmail);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetUserByProviderAsync Tests

    [Fact]
    public async Task GetUserByProviderAsync_WhenUserExists_ShouldReturnUserDto()
    {
        // Arrange
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify"
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
        var result = await _sut.GetUserByProviderAsync("spotify", "spotify123");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Email, result.Email);
        Assert.Equal("spotify", result.PrimaryProvider);
    }

    [Fact]
    public async Task GetUserByProviderAsync_WhenUserDoesNotExist_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetUserByProviderAsync("spotify", "nonexistent123");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region CreateUserAsync Tests

    [Fact]
    public async Task CreateUserAsync_WhenValidData_ShouldCreateAndReturnUser()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var displayName = "New User";
        var email = "newuser@example.com";
        var primaryProvider = "spotify";

        // Act
        var result = await _sut.CreateUserAsync(supabaseId, displayName, email, primaryProvider);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(supabaseId, result.SupabaseId);
        Assert.Equal(displayName, result.DisplayName);
        Assert.Equal(email, result.Email);
        Assert.Equal(primaryProvider, result.PrimaryProvider);

        // Verify user was saved to database
        var savedUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.SupabaseId == supabaseId);
        Assert.NotNull(savedUser);
        Assert.Equal(displayName, savedUser.DisplayName);
    }

    [Fact]
    public async Task CreateUserAsync_WhenNoPrimaryProvider_ShouldCreateUserWithNullProvider()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var displayName = "Email User";
        var email = "emailuser@example.com";

        // Act
        var result = await _sut.CreateUserAsync(supabaseId, displayName, email);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.PrimaryProvider);
    }

    #endregion

    #region UpdateUserAsync Tests

    [Fact]
    public async Task UpdateUserAsync_WhenUserExists_ShouldUpdateAndReturnUser()
    {
        // Arrange
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Original Name",
            Email = "original@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var newDisplayName = "Updated Name";
        var newEmail = "updated@example.com";

        // Act
        var result = await _sut.UpdateUserAsync(user.Id, newDisplayName, newEmail);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(newDisplayName, result.DisplayName);
        Assert.Equal(newEmail, result.Email);

        // Verify changes were saved
        var updatedUser = await _dbContext.Users.FindAsync(user.Id);
        Assert.Equal(newDisplayName, updatedUser.DisplayName);
        Assert.Equal(newEmail, updatedUser.Email);
    }

    [Fact]
    public async Task UpdateUserAsync_WhenUserDoesNotExist_ShouldThrowKeyNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateUserAsync(999, "New Name"));
    }

    [Fact]
    public async Task UpdateUserAsync_WhenOnlyDisplayNameProvided_ShouldUpdateOnlyDisplayName()
    {
        // Arrange
        var user = new User
        {
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Original Name",
            Email = "original@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var newDisplayName = "Updated Name";

        // Act
        var result = await _sut.UpdateUserAsync(user.Id, newDisplayName);

        // Assert
        Assert.Equal(newDisplayName, result.DisplayName);
        Assert.Equal("original@example.com", result.Email); // Should remain unchanged
    }

    #endregion

    #region LinkProviderAsync Tests

    [Fact]
    public async Task LinkProviderAsync_WhenNewProvider_ShouldAddProviderData()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var user = new User
        {
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var provider = "spotify";
        var providerId = "spotify123";
        var providerData = new { displayName = "Spotify User" };

        // Act
        var result = await _sut.LinkProviderAsync(supabaseId, provider, providerId, providerData);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.ProviderData);
        Assert.Equal(provider, result.ProviderData.First().Provider);
        Assert.Equal(providerId, result.ProviderData.First().ProviderId);
        Assert.Equal(provider, result.PrimaryProvider); // Should set as primary if first provider
    }

    [Fact]
    public async Task LinkProviderAsync_WhenProviderAlreadyExists_ShouldUpdateExistingProvider()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var user = new User
        {
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var existingProviderData = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "oldspotify123"
        };
        await _dbContext.UserProviderData.AddAsync(existingProviderData);
        await _dbContext.SaveChangesAsync();

        var newProviderId = "newspotify456";

        // Act
        var result = await _sut.LinkProviderAsync(supabaseId, "spotify", newProviderId);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.ProviderData);
        Assert.Equal(newProviderId, result.ProviderData.First().ProviderId);
    }

    [Fact]
    public async Task LinkProviderAsync_WhenUserDoesNotExist_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        var nonExistentSupabaseId = Guid.NewGuid().ToString();

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.LinkProviderAsync(nonExistentSupabaseId, "spotify", "spotify123"));
    }

    #endregion

    #region SetPrimaryProviderAsync Tests

    [Fact]
    public async Task SetPrimaryProviderAsync_WhenProviderIsLinked_ShouldSetAsPrimary()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var user = new User
        {
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        var providerData1 = new UserProviderData
        {
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "spotify123"
        };
        var providerData2 = new UserProviderData
        {
            UserId = user.Id,
            Provider = "apple",
            ProviderId = "apple456"
        };
        await _dbContext.UserProviderData.AddRangeAsync(providerData1, providerData2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.SetPrimaryProviderAsync(supabaseId, "apple");

        // Assert
        Assert.Equal("apple", result.PrimaryProvider);

        // Verify in database
        var updatedUser = await _dbContext.Users.FindAsync(user.Id);
        Assert.Equal("apple", updatedUser.PrimaryProvider);
    }

    [Fact]
    public async Task SetPrimaryProviderAsync_WhenProviderNotLinked_ShouldThrowArgumentException()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var user = new User
        {
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SetPrimaryProviderAsync(supabaseId, "spotify"));
    }

    [Fact]
    public async Task SetPrimaryProviderAsync_WhenUserDoesNotExist_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        var nonExistentSupabaseId = Guid.NewGuid().ToString();

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.SetPrimaryProviderAsync(nonExistentSupabaseId, "spotify"));
    }

    #endregion

    #region HasProviderLinkedAsync Tests

    [Fact]
    public async Task HasProviderLinkedAsync_WhenProviderIsLinked_ShouldReturnTrue()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var user = new User
        {
            SupabaseId = supabaseId,
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
        var result = await _sut.HasProviderLinkedAsync(supabaseId, "spotify");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasProviderLinkedAsync_WhenProviderNotLinked_ShouldReturnFalse()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var user = new User
        {
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.HasProviderLinkedAsync(supabaseId, "spotify");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetLinkedProvidersAsync Tests

    [Fact]
    public async Task GetLinkedProvidersAsync_WhenMultipleProvidersLinked_ShouldReturnAllProviders()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var user = new User
        {
            SupabaseId = supabaseId,
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
        var providerData2 = new UserProviderData
        {
            UserId = user.Id,
            Provider = "apple",
            ProviderId = "apple456"
        };
        await _dbContext.UserProviderData.AddRangeAsync(providerData1, providerData2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetLinkedProvidersAsync(supabaseId);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains("spotify", result);
        Assert.Contains("apple", result);
    }

    [Fact]
    public async Task GetLinkedProvidersAsync_WhenNoProvidersLinked_ShouldReturnEmptyCollection()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var user = new User
        {
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com"
        };
        await _dbContext.Users.AddAsync(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetLinkedProvidersAsync(supabaseId);

        // Assert
        Assert.Empty(result);
    }

    #endregion
}