using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

public interface ITokenService
{
  Task<string> GetAccessTokenAsync(int userId);
  Task<UserToken> GetUserTokenAsync(int userId);
  Task UpdateTokenAsync(UserToken token, string accessToken, string refreshToken, int expiresIn);
}
