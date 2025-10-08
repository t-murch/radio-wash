using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface IUserRepository
{
  Task<User?> GetBySupabaseIdAsync(string supabaseId);
  Task<User?> GetByIdAsync(int userId);
  Task<User?> GetByEmailAsync(string email);
  Task<User?> GetByProviderAsync(string provider, string providerId);
  Task<User> CreateAsync(User user);
  Task<User> UpdateAsync(User user);
  Task<bool> HasProviderLinkedAsync(string supabaseId, string provider);
  Task<IEnumerable<string>> GetLinkedProvidersAsync(string supabaseId);
  Task SaveChangesAsync();
}
