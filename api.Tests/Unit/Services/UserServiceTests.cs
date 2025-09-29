using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Implementations;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for UserService
/// Tests user management, provider linking, and data mapping functionality
/// </summary>
public class UserServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IUserProviderDataRepository> _mockProviderDataRepository;
    private readonly Mock<ILogger<UserService>> _mockLogger;
    private readonly UserService _userService;

    public UserServiceTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockProviderDataRepository = new Mock<IUserProviderDataRepository>();
        _mockLogger = new Mock<ILogger<UserService>>();
        
        _userService = new UserService(
            _mockUserRepository.Object,
            _mockProviderDataRepository.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetUserBySupabaseIdAsync_WithExistingUser_ReturnsUserDto()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();
        var user = CreateTestUser();
        user.SupabaseId = supabaseId.ToString();

        _mockUserRepository.Setup(x => x.GetBySupabaseIdAsync(supabaseId.ToString()))
            .ReturnsAsync(user);

        // Act
        var result = await _userService.GetUserBySupabaseIdAsync(supabaseId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.SupabaseId, result.SupabaseId);
        Assert.Equal(user.DisplayName, result.DisplayName);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task GetUserBySupabaseIdAsync_WithNonExistentUser_ReturnsNull()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();

        _mockUserRepository.Setup(x => x.GetBySupabaseIdAsync(supabaseId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.GetUserBySupabaseIdAsync(supabaseId);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("User not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetUserBySupabaseIdAsync_WithRepositoryException_RethrowsException()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();
        var exception = new Exception("Database error");

        _mockUserRepository.Setup(x => x.GetBySupabaseIdAsync(supabaseId.ToString()))
            .ThrowsAsync(exception);

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<Exception>(() => 
            _userService.GetUserBySupabaseIdAsync(supabaseId));

        Assert.Equal(exception, thrownException);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error retrieving user")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetUserByEmailAsync_WithExistingUser_ReturnsUserDto()
    {
        // Arrange
        var email = "test@example.com";
        var user = CreateTestUser();
        user.Email = email;

        _mockUserRepository.Setup(x => x.GetByEmailAsync(email))
            .ReturnsAsync(user);

        // Act
        var result = await _userService.GetUserByEmailAsync(email);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task GetUserByEmailAsync_WithNonExistentUser_ReturnsNull()
    {
        // Arrange
        var email = "nonexistent@example.com";

        _mockUserRepository.Setup(x => x.GetByEmailAsync(email))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.GetUserByEmailAsync(email);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateUserAsync_WithValidData_ReturnsUserDto()
    {
        // Arrange
        var supabaseId = "sb_123";
        var displayName = "Test User";
        var email = "test@example.com";
        var primaryProvider = "spotify";

        var createdUser = CreateTestUser();
        createdUser.SupabaseId = supabaseId;
        createdUser.DisplayName = displayName;
        createdUser.Email = email;
        createdUser.PrimaryProvider = primaryProvider;

        _mockUserRepository.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(createdUser);

        // Act
        var result = await _userService.CreateUserAsync(supabaseId, displayName, email, primaryProvider);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(supabaseId, result.SupabaseId);
        Assert.Equal(displayName, result.DisplayName);
        Assert.Equal(email, result.Email);
        Assert.Equal(primaryProvider, result.PrimaryProvider);

        _mockUserRepository.Verify(x => x.CreateAsync(It.Is<User>(u =>
            u.SupabaseId == supabaseId &&
            u.DisplayName == displayName &&
            u.Email == email &&
            u.PrimaryProvider == primaryProvider
        )), Times.Once);
    }

    [Fact]
    public async Task CreateUserAsync_WithNullPrimaryProvider_CreatesUserWithoutProvider()
    {
        // Arrange
        var supabaseId = "sb_123";
        var displayName = "Test User";
        var email = "test@example.com";

        var createdUser = CreateTestUser();
        createdUser.SupabaseId = supabaseId;
        createdUser.DisplayName = displayName;
        createdUser.Email = email;
        createdUser.PrimaryProvider = null;

        _mockUserRepository.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(createdUser);

        // Act
        var result = await _userService.CreateUserAsync(supabaseId, displayName, email);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.PrimaryProvider);
    }

    [Fact]
    public async Task UpdateUserAsync_WithExistingUser_UpdatesAndReturnsUserDto()
    {
        // Arrange
        var userId = 1;
        var newDisplayName = "Updated Name";
        var newEmail = "updated@example.com";

        var existingUser = CreateTestUser();
        existingUser.Id = userId;

        var updatedUser = CreateTestUser();
        updatedUser.Id = userId;
        updatedUser.DisplayName = newDisplayName;
        updatedUser.Email = newEmail;

        _mockUserRepository.Setup(x => x.GetByIdAsync(userId))
            .ReturnsAsync(existingUser);
        _mockUserRepository.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(updatedUser);

        // Act
        var result = await _userService.UpdateUserAsync(userId, newDisplayName, newEmail);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(newDisplayName, result.DisplayName);
        Assert.Equal(newEmail, result.Email);

        _mockUserRepository.Verify(x => x.UpdateAsync(It.Is<User>(u =>
            u.DisplayName == newDisplayName &&
            u.Email == newEmail
        )), Times.Once);
    }

    [Fact]
    public async Task UpdateUserAsync_WithNonExistentUser_ThrowsKeyNotFoundException()
    {
        // Arrange
        var userId = 999;

        _mockUserRepository.Setup(x => x.GetByIdAsync(userId))
            .ReturnsAsync((User?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _userService.UpdateUserAsync(userId, "New Name"));

        Assert.Contains($"User with ID {userId} not found", exception.Message);
    }

    [Fact]
    public async Task UpdateUserAsync_WithPartialUpdate_OnlyUpdatesProvidedFields()
    {
        // Arrange
        var userId = 1;
        var newDisplayName = "Updated Name";
        var originalEmail = "original@example.com";

        var existingUser = CreateTestUser();
        existingUser.Id = userId;
        existingUser.Email = originalEmail;

        var updatedUser = CreateTestUser();
        updatedUser.Id = userId;
        updatedUser.DisplayName = newDisplayName;
        updatedUser.Email = originalEmail;

        _mockUserRepository.Setup(x => x.GetByIdAsync(userId))
            .ReturnsAsync(existingUser);
        _mockUserRepository.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(updatedUser);

        // Act
        var result = await _userService.UpdateUserAsync(userId, displayName: newDisplayName);

        // Assert
        Assert.Equal(newDisplayName, result.DisplayName);
        Assert.Equal(originalEmail, result.Email);

        _mockUserRepository.Verify(x => x.UpdateAsync(It.Is<User>(u =>
            u.DisplayName == newDisplayName &&
            u.Email == originalEmail
        )), Times.Once);
    }

    [Fact]
    public async Task LinkProviderAsync_WithNewProvider_CreatesProviderData()
    {
        // Arrange
        var supabaseId = "sb_123";
        var provider = "spotify";
        var providerId = "spotify_123";
        var providerData = new { name = "Test User" };

        var user = CreateTestUser();
        user.SupabaseId = supabaseId;

        _mockUserRepository.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(user);
        _mockProviderDataRepository.Setup(x => x.GetByUserAndProviderAsync(user.Id, provider))
            .ReturnsAsync((UserProviderData?)null);

        // Act
        var result = await _userService.LinkProviderAsync(supabaseId, provider, providerId, providerData);

        // Assert
        Assert.NotNull(result);

        _mockProviderDataRepository.Verify(x => x.CreateAsync(It.Is<UserProviderData>(pd =>
            pd.UserId == user.Id &&
            pd.Provider == provider &&
            pd.ProviderId == providerId &&
            pd.ProviderMetadata != null
        )), Times.Once);
    }

    [Fact]
    public async Task LinkProviderAsync_WithExistingProvider_UpdatesProviderData()
    {
        // Arrange
        var supabaseId = "sb_123";
        var provider = "spotify";
        var providerId = "spotify_456";

        var user = CreateTestUser();
        user.SupabaseId = supabaseId;

        var existingProviderData = new UserProviderData
        {
            Id = 1,
            UserId = user.Id,
            Provider = provider,
            ProviderId = "spotify_123"
        };

        _mockUserRepository.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(user);
        _mockProviderDataRepository.Setup(x => x.GetByUserAndProviderAsync(user.Id, provider))
            .ReturnsAsync(existingProviderData);

        // Act
        var result = await _userService.LinkProviderAsync(supabaseId, provider, providerId);

        // Assert
        _mockProviderDataRepository.Verify(x => x.UpdateAsync(It.Is<UserProviderData>(pd =>
            pd.ProviderId == providerId
        )), Times.Once);
    }

    [Fact]
    public async Task LinkProviderAsync_WithUserWithoutPrimaryProvider_SetsPrimaryProvider()
    {
        // Arrange
        var supabaseId = "sb_123";
        var provider = "spotify";
        var providerId = "spotify_123";

        var user = CreateTestUser();
        user.SupabaseId = supabaseId;
        user.PrimaryProvider = null;

        _mockUserRepository.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(user);
        _mockProviderDataRepository.Setup(x => x.GetByUserAndProviderAsync(user.Id, provider))
            .ReturnsAsync((UserProviderData?)null);

        // Act
        await _userService.LinkProviderAsync(supabaseId, provider, providerId);

        // Assert
        _mockUserRepository.Verify(x => x.UpdateAsync(It.Is<User>(u =>
            u.PrimaryProvider == provider
        )), Times.Once);
    }

    [Fact]
    public async Task LinkProviderAsync_WithNonExistentUser_ThrowsKeyNotFoundException()
    {
        // Arrange
        var supabaseId = "sb_nonexistent";
        var provider = "spotify";
        var providerId = "spotify_123";

        _mockUserRepository.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync((User?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _userService.LinkProviderAsync(supabaseId, provider, providerId));

        Assert.Contains($"User with Supabase ID {supabaseId} not found", exception.Message);
    }

    [Fact]
    public async Task SetPrimaryProviderAsync_WithLinkedProvider_UpdatesPrimaryProvider()
    {
        // Arrange
        var supabaseId = "sb_123";
        var provider = "spotify";

        var user = CreateTestUser();
        user.SupabaseId = supabaseId;
        user.PrimaryProvider = "google";

        var updatedUser = CreateTestUser();
        updatedUser.SupabaseId = supabaseId;
        updatedUser.PrimaryProvider = provider;

        _mockUserRepository.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(user);
        _mockUserRepository.Setup(x => x.HasProviderLinkedAsync(supabaseId, provider))
            .ReturnsAsync(true);
        _mockUserRepository.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(updatedUser);

        // Act
        var result = await _userService.SetPrimaryProviderAsync(supabaseId, provider);

        // Assert
        Assert.Equal(provider, result.PrimaryProvider);

        _mockUserRepository.Verify(x => x.UpdateAsync(It.Is<User>(u =>
            u.PrimaryProvider == provider
        )), Times.Once);
    }

    [Fact]
    public async Task SetPrimaryProviderAsync_WithUnlinkedProvider_ThrowsArgumentException()
    {
        // Arrange
        var supabaseId = "sb_123";
        var provider = "spotify";

        var user = CreateTestUser();
        user.SupabaseId = supabaseId;

        _mockUserRepository.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(user);
        _mockUserRepository.Setup(x => x.HasProviderLinkedAsync(supabaseId, provider))
            .ReturnsAsync(false);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _userService.SetPrimaryProviderAsync(supabaseId, provider));

        Assert.Contains($"Provider {provider} is not linked", exception.Message);
    }

    [Fact]
    public async Task HasProviderLinkedAsync_ReturnsRepositoryResult()
    {
        // Arrange
        var supabaseId = "sb_123";
        var provider = "spotify";

        _mockUserRepository.Setup(x => x.HasProviderLinkedAsync(supabaseId, provider))
            .ReturnsAsync(true);

        // Act
        var result = await _userService.HasProviderLinkedAsync(supabaseId, provider);

        // Assert
        Assert.True(result);
        _mockUserRepository.Verify(x => x.HasProviderLinkedAsync(supabaseId, provider), Times.Once);
    }

    [Fact]
    public async Task GetLinkedProvidersAsync_ReturnsRepositoryResult()
    {
        // Arrange
        var supabaseId = "sb_123";
        var providers = new[] { "spotify", "google" };

        _mockUserRepository.Setup(x => x.GetLinkedProvidersAsync(supabaseId))
            .ReturnsAsync(providers);

        // Act
        var result = await _userService.GetLinkedProvidersAsync(supabaseId);

        // Assert
        Assert.Equal(providers, result);
        _mockUserRepository.Verify(x => x.GetLinkedProvidersAsync(supabaseId), Times.Once);
    }

    private static User CreateTestUser()
    {
        return new User
        {
            Id = 1,
            SupabaseId = "sb_test",
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "email",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
            ProviderData = new List<UserProviderData>()
        };
    }
}