using RadioWash.Api.Models.DTO;

namespace RadioWash.Api.Services.Interfaces;

public interface IUserService
{
  // Core user operations
  Task<UserDto?> GetUserBySupabaseIdAsync(Guid supabaseId);
  Task<UserDto?> GetUserByEmailAsync(string email);
  Task<UserDto> CreateUserAsync(string supabaseId, string displayName, string email, string? primaryProvider = null);
  Task<UserDto> UpdateUserAsync(int userId, string? displayName = null, string? email = null);

  // Multi-provider support
  Task<UserDto> LinkProviderAsync(string supabaseId, string provider, string providerId, object? providerData = null);
  Task<UserDto> SetPrimaryProviderAsync(string supabaseId, string provider);
  Task<bool> HasProviderLinkedAsync(string supabaseId, string provider);
  Task<IEnumerable<string>> GetLinkedProvidersAsync(string supabaseId);

  // Provider-specific user lookup
  Task<UserDto?> GetUserByProviderAsync(string provider, string providerId);
}
