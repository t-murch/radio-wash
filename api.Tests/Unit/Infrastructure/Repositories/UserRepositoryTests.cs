using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Unit.Infrastructure;

namespace RadioWash.Api.Tests.Unit.Infrastructure.Repositories;

/// <summary>
/// Unit tests for UserRepository
/// Tests user CRUD operations, provider lookups, and relationship management
/// </summary>
public class UserRepositoryTests : RepositoryTestBase
{
  private readonly UserRepository _userRepository;

  public UserRepositoryTests()
  {
    _userRepository = new UserRepository(_context);
  }

  [Fact]
  public async Task GetBySupabaseIdAsync_WithExistingUser_ReturnsUserWithProviderData()
  {
    // Arrange
    var user = CreateTestUser(supabaseId: "sb_test_123");
    await SeedAsync(user);

    var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
    await SeedAsync(providerData);

    // Act
    var result = await _userRepository.GetBySupabaseIdWithProvidersAsync("sb_test_123");

    // Assert
    Assert.NotNull(result);
    Assert.Equal("sb_test_123", result.SupabaseId);
    Assert.Equal("Test User", result.DisplayName);
    Assert.Equal("test@example.com", result.Email);
    Assert.Single(result.ProviderData);
    Assert.Equal("spotify", result.ProviderData.First().Provider);
  }

  [Fact]
  public async Task GetBySupabaseIdAsync_WithNonExistentUser_ReturnsNull()
  {
    // Arrange
    var user = CreateTestUser(supabaseId: "sb_other_123");
    await SeedAsync(user);

    // Act
    var result = await _userRepository.GetBySupabaseIdAsync("sb_nonexistent");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetByIdAsync_WithExistingUser_ReturnsUserWithProviderData()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var providerData = CreateTestUserProviderData(user.Id, "google", "google_456");
    await SeedAsync(providerData);

    // Act
    var result = await _userRepository.GetByIdWithProvidersAsync(user.Id);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(user.Id, result.Id);
    Assert.Equal(user.SupabaseId, result.SupabaseId);
    Assert.Single(result.ProviderData);
    Assert.Equal("google", result.ProviderData.First().Provider);
  }

  [Fact]
  public async Task GetByIdAsync_WithNonExistentUser_ReturnsNull()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    // Act
    var result = await _userRepository.GetByIdAsync(999);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetByEmailAsync_WithExistingUser_ReturnsUserWithProviderData()
  {
    // Arrange
    var user = CreateTestUser(email: "unique@example.com");
    await SeedAsync(user);

    var providerData = CreateTestUserProviderData(user.Id, "apple", "apple_789");
    await SeedAsync(providerData);

    // Act
    var result = await _userRepository.GetByEmailWithProvidersAsync("unique@example.com");

    // Assert
    Assert.NotNull(result);
    Assert.Equal("unique@example.com", result.Email);
    Assert.Equal(user.SupabaseId, result.SupabaseId);
    Assert.Single(result.ProviderData);
    Assert.Equal("apple", result.ProviderData.First().Provider);
  }

  [Fact]
  public async Task GetByEmailAsync_WithNonExistentUser_ReturnsNull()
  {
    // Arrange
    var user = CreateTestUser(email: "existing@example.com");
    await SeedAsync(user);

    // Act
    var result = await _userRepository.GetByEmailAsync("nonexistent@example.com");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetByProviderAsync_WithExistingProviderData_ReturnsUserWithProviderData()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_unique_123");
    await SeedAsync(providerData);

    // Act
    var result = await _userRepository.GetByProviderAsync("spotify", "spotify_unique_123");

    // Assert
    Assert.NotNull(result);
    Assert.Equal(user.Id, result.Id);
    Assert.Equal(user.SupabaseId, result.SupabaseId);
    Assert.Single(result.ProviderData);
    Assert.Equal("spotify_unique_123", result.ProviderData.First().ProviderId);
  }

  [Fact]
  public async Task GetByProviderAsync_WithNonExistentProviderData_ReturnsNull()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
    await SeedAsync(providerData);

    // Act
    var result = await _userRepository.GetByProviderAsync("spotify", "nonexistent_provider_id");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetByProviderAsync_WithDifferentProvider_ReturnsNull()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
    await SeedAsync(providerData);

    // Act
    var result = await _userRepository.GetByProviderAsync("google", "spotify_123");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task CreateAsync_WithValidUser_CreatesUserSuccessfully()
  {
    // Arrange
    var user = CreateTestUser(
        supabaseId: "sb_new_user",
        displayName: "New User",
        email: "newuser@example.com");

    // Act
    var result = await _userRepository.CreateAsync(user);

    // Assert
    Assert.NotNull(result);
    Assert.True(result.Id > 0);
    Assert.Equal("sb_new_user", result.SupabaseId);
    Assert.Equal("New User", result.DisplayName);
    Assert.Equal("newuser@example.com", result.Email);

    // Verify it was saved to database
    var savedUser = await _context.Users.FindAsync(result.Id);
    Assert.NotNull(savedUser);
    Assert.Equal("sb_new_user", savedUser.SupabaseId);
  }

  [Fact]
  public async Task UpdateAsync_WithValidUser_UpdatesUserAndTimestamp()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var originalUpdatedAt = user.UpdatedAt;

    // Detach to simulate fresh load
    DetachAllEntities();

    var userToUpdate = await _context.Users.FindAsync(user.Id);
    Assert.NotNull(userToUpdate);

    userToUpdate.DisplayName = "Updated Name";
    userToUpdate.Email = "updated@example.com";

    // Act
    var result = await _userRepository.UpdateAsync(userToUpdate);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("Updated Name", result.DisplayName);
    Assert.Equal("updated@example.com", result.Email);
    Assert.True(result.UpdatedAt > originalUpdatedAt);

    // Verify it was updated in database
    DetachAllEntities();
    var updatedUser = await _context.Users.FindAsync(user.Id);
    Assert.NotNull(updatedUser);
    Assert.Equal("Updated Name", updatedUser.DisplayName);
    Assert.Equal("updated@example.com", updatedUser.Email);
  }

  [Fact]
  public async Task HasProviderLinkedAsync_WithLinkedProvider_ReturnsTrue()
  {
    // Arrange
    var user = CreateTestUser(supabaseId: "sb_linked_test");
    await SeedAsync(user);

    var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
    await SeedAsync(providerData);

    // Act
    var result = await _userRepository.HasProviderLinkedAsync("sb_linked_test", "spotify");

    // Assert
    Assert.True(result);
  }

  [Fact]
  public async Task HasProviderLinkedAsync_WithUnlinkedProvider_ReturnsFalse()
  {
    // Arrange
    var user = CreateTestUser(supabaseId: "sb_unlinked_test");
    await SeedAsync(user);

    var providerData = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
    await SeedAsync(providerData);

    // Act
    var result = await _userRepository.HasProviderLinkedAsync("sb_unlinked_test", "google");

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task HasProviderLinkedAsync_WithNonExistentUser_ReturnsFalse()
  {
    // Arrange
    var user = CreateTestUser(supabaseId: "sb_existing");
    await SeedAsync(user);

    // Act
    var result = await _userRepository.HasProviderLinkedAsync("sb_nonexistent", "spotify");

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task GetLinkedProvidersAsync_WithMultipleProviders_ReturnsAllProviders()
  {
    // Arrange
    var user = CreateTestUser(supabaseId: "sb_multi_provider");
    await SeedAsync(user);

    var spotifyProvider = CreateTestUserProviderData(user.Id, "spotify", "spotify_123");
    var googleProvider = CreateTestUserProviderData(user.Id, "google", "google_456");
    var appleProvider = CreateTestUserProviderData(user.Id, "apple", "apple_789");
    await SeedAsync(spotifyProvider, googleProvider, appleProvider);

    // Act
    var result = await _userRepository.GetLinkedProvidersAsync("sb_multi_provider");

    // Assert
    var providers = result.ToList();
    Assert.Equal(3, providers.Count);
    Assert.Contains("spotify", providers);
    Assert.Contains("google", providers);
    Assert.Contains("apple", providers);
  }

  [Fact]
  public async Task GetLinkedProvidersAsync_WithNoProviders_ReturnsEmptyList()
  {
    // Arrange
    var user = CreateTestUser(supabaseId: "sb_no_providers");
    await SeedAsync(user);

    // Act
    var result = await _userRepository.GetLinkedProvidersAsync("sb_no_providers");

    // Assert
    var providers = result.ToList();
    Assert.Empty(providers);
  }

  [Fact]
  public async Task GetLinkedProvidersAsync_WithNonExistentUser_ReturnsEmptyList()
  {
    // Arrange
    var user = CreateTestUser(supabaseId: "sb_existing");
    await SeedAsync(user);

    // Act
    var result = await _userRepository.GetLinkedProvidersAsync("sb_nonexistent");

    // Assert
    var providers = result.ToList();
    Assert.Empty(providers);
  }

  [Fact]
  public async Task SaveChangesAsync_WithPendingChanges_SavesChanges()
  {
    // Arrange
    var user = CreateTestUser();
    _context.Users.Add(user);

    // Act
    await _userRepository.SaveChangesAsync();

    // Assert
    Assert.True(user.Id > 0);

    // Verify it was saved
    var savedUser = await _context.Users.FindAsync(user.Id);
    Assert.NotNull(savedUser);
    Assert.Equal(user.SupabaseId, savedUser.SupabaseId);
  }
}
