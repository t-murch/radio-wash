using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IUserProviderDataRepository> _providerDataRepositoryMock;
    private readonly Mock<ILogger<UserService>> _loggerMock;
    private readonly UserService _sut;

    public UserServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _providerDataRepositoryMock = new Mock<IUserProviderDataRepository>();
        _loggerMock = new Mock<ILogger<UserService>>();
        
        _sut = new UserService(_userRepositoryMock.Object, _providerDataRepositoryMock.Object, _loggerMock.Object);
    }

    #region GetUserBySupabaseIdAsync Tests

    [Fact]
    public async Task GetUserBySupabaseIdAsync_WhenUserExists_ShouldReturnUserDto()
    {
        // Arrange
        var supabaseId = Guid.NewGuid();
        var user = new User
        {
            Id = 1,
            SupabaseId = supabaseId.ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ProviderData = new List<UserProviderData>()
        };
        
        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(supabaseId.ToString()))
            .ReturnsAsync(user);

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
        
        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(nonExistentSupabaseId.ToString()))
            .ReturnsAsync((User?)null);

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
        var providerData = new UserProviderData
        {
            Id = 1,
            UserId = 1,
            Provider = "spotify",
            ProviderId = "spotify123",
            ProviderMetadata = "{\"displayName\":\"Spotify User\"}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        var user = new User
        {
            Id = 1,
            SupabaseId = supabaseId.ToString(),
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ProviderData = new List<UserProviderData> { providerData }
        };

        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(supabaseId.ToString()))
            .ReturnsAsync(user);

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
            PrimaryProvider = "email",
            ProviderData = new List<UserProviderData>()
        };

        _userRepositoryMock.Setup(x => x.GetByEmailAsync(email))
            .ReturnsAsync(user);

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

        _userRepositoryMock.Setup(x => x.GetByEmailAsync(nonExistentEmail))
            .ReturnsAsync((User?)null);

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
            PrimaryProvider = "spotify",
            ProviderData = new List<UserProviderData>()
        };

        _userRepositoryMock.Setup(x => x.GetByProviderAsync("spotify", "spotify123"))
            .ReturnsAsync(user);

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
        // Arrange
        _userRepositoryMock.Setup(x => x.GetByProviderAsync("spotify", "nonexistent123"))
            .ReturnsAsync((User?)null);

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
        
        var expectedUser = new User
        {
            Id = 1,
            SupabaseId = supabaseId,
            DisplayName = displayName,
            Email = email,
            PrimaryProvider = primaryProvider,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ProviderData = new List<UserProviderData>()
        };

        _userRepositoryMock.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(expectedUser);

        // Act
        var result = await _sut.CreateUserAsync(supabaseId, displayName, email, primaryProvider);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(supabaseId, result.SupabaseId);
        Assert.Equal(displayName, result.DisplayName);
        Assert.Equal(email, result.Email);
        Assert.Equal(primaryProvider, result.PrimaryProvider);
        
        // Verify repository method was called
        _userRepositoryMock.Verify(x => x.CreateAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task CreateUserAsync_WhenNoPrimaryProvider_ShouldCreateUserWithNullProvider()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var displayName = "Email User";
        var email = "emailuser@example.com";

        var expectedUser = new User
        {
            Id = 1,
            SupabaseId = supabaseId,
            DisplayName = displayName,
            Email = email,
            PrimaryProvider = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ProviderData = new List<UserProviderData>()
        };

        _userRepositoryMock.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(expectedUser);

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
        var userId = 1;
        var user = new User
        {
            Id = userId,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Original Name",
            Email = "original@example.com",
            ProviderData = new List<UserProviderData>()
        };

        var newDisplayName = "Updated Name";
        var newEmail = "updated@example.com";

        _userRepositoryMock.Setup(x => x.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _userRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => 
            {
                u.DisplayName = newDisplayName;
                u.Email = newEmail;
                return u;
            });

        // Act
        var result = await _sut.UpdateUserAsync(userId, newDisplayName, newEmail);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(newDisplayName, result.DisplayName);
        Assert.Equal(newEmail, result.Email);
        _userRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task UpdateUserAsync_WhenUserDoesNotExist_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        _userRepositoryMock.Setup(x => x.GetByIdAsync(999))
            .ReturnsAsync((User?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateUserAsync(999, "New Name"));
    }

    [Fact]
    public async Task UpdateUserAsync_WhenOnlyDisplayNameProvided_ShouldUpdateOnlyDisplayName()
    {
        // Arrange
        var userId = 1;
        var originalEmail = "original@example.com";
        var user = new User
        {
            Id = userId,
            SupabaseId = Guid.NewGuid().ToString(),
            DisplayName = "Original Name",
            Email = originalEmail,
            ProviderData = new List<UserProviderData>()
        };

        var newDisplayName = "Updated Name";

        _userRepositoryMock.Setup(x => x.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _userRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => 
            {
                u.DisplayName = newDisplayName;
                return u;
            });

        // Act
        var result = await _sut.UpdateUserAsync(userId, newDisplayName);

        // Assert
        Assert.Equal(newDisplayName, result.DisplayName);
        Assert.Equal(originalEmail, result.Email); // Should remain unchanged
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
            Id = 1,
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com",
            ProviderData = new List<UserProviderData>()
        };

        var provider = "spotify";
        var providerId = "spotify123";
        var providerData = new { displayName = "Spotify User" };

        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(user);

        _providerDataRepositoryMock.Setup(x => x.GetByUserAndProviderAsync(user.Id, provider))
            .ReturnsAsync((UserProviderData?)null);

        _providerDataRepositoryMock.Setup(x => x.CreateAsync(It.IsAny<UserProviderData>()))
            .ReturnsAsync(new UserProviderData
            {
                Id = 1,
                UserId = user.Id,
                Provider = provider,
                ProviderId = providerId
            });

        _userRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => 
            {
                u.PrimaryProvider = provider;
                return u;
            });

        var updatedUser = new User
        {
            Id = 1,
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = provider,
            ProviderData = new List<UserProviderData>
            {
                new UserProviderData
                {
                    Id = 1,
                    UserId = 1,
                    Provider = provider,
                    ProviderId = providerId
                }
            }
        };

        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(updatedUser);

        // Act
        var result = await _sut.LinkProviderAsync(supabaseId, provider, providerId, providerData);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.ProviderData);
        Assert.Equal(provider, result.ProviderData.First().Provider);
        Assert.Equal(providerId, result.ProviderData.First().ProviderId);
        Assert.Equal(provider, result.PrimaryProvider);
    }

    [Fact]
    public async Task LinkProviderAsync_WhenProviderAlreadyExists_ShouldUpdateExistingProvider()
    {
        // Arrange
        var supabaseId = Guid.NewGuid().ToString();
        var user = new User
        {
            Id = 1,
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify",
            ProviderData = new List<UserProviderData>()
        };

        var existingProviderData = new UserProviderData
        {
            Id = 1,
            UserId = user.Id,
            Provider = "spotify",
            ProviderId = "oldspotify123"
        };

        var newProviderId = "newspotify456";

        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(user);

        _providerDataRepositoryMock.Setup(x => x.GetByUserAndProviderAsync(user.Id, "spotify"))
            .ReturnsAsync(existingProviderData);

        _providerDataRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserProviderData>()))
            .ReturnsAsync((UserProviderData upd) =>
            {
                upd.ProviderId = newProviderId;
                return upd;
            });

        var updatedUser = new User
        {
            Id = 1,
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify",
            ProviderData = new List<UserProviderData>
            {
                new UserProviderData
                {
                    Id = 1,
                    UserId = 1,
                    Provider = "spotify",
                    ProviderId = newProviderId
                }
            }
        };

        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(updatedUser);

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

        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(nonExistentSupabaseId))
            .ReturnsAsync((User?)null);

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
            Id = 1,
            SupabaseId = supabaseId,
            DisplayName = "Test User",
            Email = "test@example.com",
            PrimaryProvider = "spotify",
            ProviderData = new List<UserProviderData>()
        };

        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(user);

        _userRepositoryMock.Setup(x => x.HasProviderLinkedAsync(supabaseId, "apple"))
            .ReturnsAsync(true);

        _userRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) =>
            {
                u.PrimaryProvider = "apple";
                return u;
            });

        // Act
        var result = await _sut.SetPrimaryProviderAsync(supabaseId, "apple");

        // Assert
        Assert.Equal("apple", result.PrimaryProvider);
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
            Email = "test@example.com",
            ProviderData = new List<UserProviderData>()
        };

        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(supabaseId))
            .ReturnsAsync(user);

        _userRepositoryMock.Setup(x => x.HasProviderLinkedAsync(supabaseId, "spotify"))
            .ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SetPrimaryProviderAsync(supabaseId, "spotify"));
    }

    [Fact]
    public async Task SetPrimaryProviderAsync_WhenUserDoesNotExist_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        var nonExistentSupabaseId = Guid.NewGuid().ToString();

        _userRepositoryMock.Setup(x => x.GetBySupabaseIdAsync(nonExistentSupabaseId))
            .ReturnsAsync((User?)null);

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

        _userRepositoryMock.Setup(x => x.HasProviderLinkedAsync(supabaseId, "spotify"))
            .ReturnsAsync(true);

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

        _userRepositoryMock.Setup(x => x.HasProviderLinkedAsync(supabaseId, "spotify"))
            .ReturnsAsync(false);

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
        var providers = new List<string> { "spotify", "apple" };

        _userRepositoryMock.Setup(x => x.GetLinkedProvidersAsync(supabaseId))
            .ReturnsAsync(providers);

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

        _userRepositoryMock.Setup(x => x.GetLinkedProvidersAsync(supabaseId))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _sut.GetLinkedProvidersAsync(supabaseId);

        // Assert
        Assert.Empty(result);
    }

    #endregion
}