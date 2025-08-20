using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public class UserMusicTokenRepository : IUserMusicTokenRepository
{
    private readonly RadioWashDbContext _dbContext;

    public UserMusicTokenRepository(RadioWashDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserMusicToken?> GetByUserAndProviderAsync(int userId, string provider)
    {
        return await _dbContext.UserMusicTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == provider && !t.IsRevoked);
    }

    public async Task<UserMusicToken> CreateAsync(UserMusicToken token)
    {
        _dbContext.UserMusicTokens.Add(token);
        await _dbContext.SaveChangesAsync();
        return token;
    }

    public async Task<UserMusicToken> UpdateAsync(UserMusicToken token)
    {
        token.UpdatedAt = DateTime.UtcNow;
        _dbContext.UserMusicTokens.Update(token);
        await _dbContext.SaveChangesAsync();
        return token;
    }

    public async Task DeleteAsync(UserMusicToken token)
    {
        _dbContext.UserMusicTokens.Remove(token);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> HasValidTokensAsync(int userId, string provider)
    {
        var tokenRecord = await GetByUserAndProviderAsync(userId, provider);
        if (tokenRecord == null)
        {
            return false;
        }

        // Token is valid if not expired or we have a refresh token
        return DateTime.UtcNow < tokenRecord.ExpiresAt || !string.IsNullOrEmpty(tokenRecord.EncryptedRefreshToken);
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}