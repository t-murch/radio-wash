using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface IUserProviderDataRepository
{
  Task<UserProviderData?> GetByProviderAsync(string provider, string providerId);
  Task<UserProviderData?> GetByUserAndProviderAsync(int userId, string provider);
  Task<IEnumerable<UserProviderData>> GetByUserIdAsync(int userId);
  Task<UserProviderData> CreateAsync(UserProviderData providerData);
  Task<UserProviderData> UpdateAsync(UserProviderData providerData);
  Task DeleteAsync(UserProviderData providerData);
  Task SaveChangesAsync();
}
