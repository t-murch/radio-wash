// api/Services/Implementations/TokenService.cs
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class TokenService : ITokenService
{
  private readonly RadioWashDbContext _dbContext;

  public TokenService(RadioWashDbContext dbContext)
  {
    _dbContext = dbContext;
  }

  public async Task<string> GetAccessTokenAsync(int userId)
  {
    var token = await GetUserTokenAsync(userId);

    if (token == null)
    {
      throw new Exception("User token not found");
    }

    return token.AccessToken;
  }

  public async Task<UserToken> GetUserTokenAsync(int userId)
  {
    var token = await _dbContext.UserTokens
        .FirstOrDefaultAsync(t => t.UserId == userId);

    return token;
  }

  public async Task UpdateTokenAsync(UserToken token, string accessToken, string refreshToken, int expiresIn)
  {
    token.AccessToken = accessToken;
    token.RefreshToken = refreshToken;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
    token.UpdatedAt = DateTime.UtcNow;

    _dbContext.UserTokens.Update(token);
    await _dbContext.SaveChangesAsync();
  }
}
