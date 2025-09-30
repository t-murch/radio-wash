using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface IUserMusicTokenRepository
{
  Task<UserMusicToken?> GetByUserAndProviderAsync(int userId, string provider);
  Task<UserMusicToken> CreateAsync(UserMusicToken token);
  Task<UserMusicToken> UpdateAsync(UserMusicToken token);
  Task DeleteAsync(UserMusicToken token);
  Task<bool> HasValidTokensAsync(int userId, string provider);
  Task SaveChangesAsync();
}
