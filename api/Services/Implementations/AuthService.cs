using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RadioWash.Api.Configuration;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Models.Spotify;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class AuthService : IAuthService
{
  private readonly HttpClient _httpClient;
  private readonly SpotifySettings _spotifySettings;
  private readonly JwtSettings _jwtSettings;
  private readonly RadioWashDbContext _dbContext;
  private readonly ITokenService _tokenService;

  public AuthService(
      HttpClient httpClient,
      IOptions<SpotifySettings> spotifySettings,
      IOptions<JwtSettings> jwtSettings,
      RadioWashDbContext dbContext,
      ITokenService tokenService)
  {
    _httpClient = httpClient;
    _spotifySettings = spotifySettings.Value;
    _jwtSettings = jwtSettings.Value;
    _dbContext = dbContext;
    _tokenService = tokenService;
  }

  public string GenerateAuthUrl(string state)
  {
    var scopes = string.Join(" ", _spotifySettings.Scopes);
    var queryParams = new Dictionary<string, string>
        {
            { "client_id", _spotifySettings.ClientId },
            { "response_type", "code" },
            { "redirect_uri", _spotifySettings.RedirectUri },
            { "state", state },
            { "scope", scopes },
            { "show_dialog", "true" }
        };

    var queryString = string.Join("&", queryParams.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    return $"{_spotifySettings.AuthUrl}?{queryString}";
  }

  public async Task<AuthResponseDto> HandleCallbackAsync(string code)
  {
    var authResponse = await GetTokensFromCodeAsync(code);
    var userProfile = await GetUserProfileAsync(authResponse.AccessToken);
    var user = await GetOrCreateUserAsync(userProfile, authResponse);
    var jwtToken = await GenerateJwtTokenAsync(user);

    return new AuthResponseDto
    {
      Token = jwtToken,
      User = new UserDto
      {
        Id = user.Id,
        SpotifyId = user.SpotifyId,
        DisplayName = user.DisplayName,
        Email = user.Email,
        ProfileImageUrl = userProfile.Images?.FirstOrDefault()?.Url
      }
    };
  }

  public async Task<User> GetOrCreateUserAsync(SpotifyUserProfile profile, SpotifyAuthResponse tokens)
  {
    var user = await _dbContext.Users
        .Include(u => u.Token)
        .FirstOrDefaultAsync(u => u.SpotifyId == profile.Id);

    if (user == null)
    {
      user = new User
      {
        SpotifyId = profile.Id,
        DisplayName = profile.DisplayName,
        Email = profile.Email
      };

      _dbContext.Users.Add(user);
      await _dbContext.SaveChangesAsync();

      // Create user token
      var userToken = new UserToken
      {
        UserId = user.Id,
        AccessToken = tokens.AccessToken,
        RefreshToken = tokens.RefreshToken,
        ExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn)
      };

      _dbContext.UserTokens.Add(userToken);
      await _dbContext.SaveChangesAsync();
    }
    else
    {
      // Update user information
      user.DisplayName = profile.DisplayName;
      user.Email = profile.Email;
      user.UpdatedAt = DateTime.UtcNow;

      // Update token if exists, or create a new one
      if (user.Token != null)
      {
        await _tokenService.UpdateTokenAsync(
            user.Token,
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresIn
        );
      }
      else
      {
        var userToken = new UserToken
        {
          UserId = user.Id,
          AccessToken = tokens.AccessToken,
          RefreshToken = tokens.RefreshToken,
          ExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn)
        };

        _dbContext.UserTokens.Add(userToken);
      }

      await _dbContext.SaveChangesAsync();
    }

    return user;
  }

  public async Task<string> GenerateJwtTokenAsync(User user)
  {
    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.ASCII.GetBytes(_jwtSettings.Secret);

    var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, user.SpotifyId)
        };

    var tokenDescriptor = new SecurityTokenDescriptor
    {
      Subject = new ClaimsIdentity(claims),
      Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationInMinutes),
      Issuer = _jwtSettings.Issuer,
      Audience = _jwtSettings.Audience,
      SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256Signature)
    };

    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
  }

  public async Task<bool> ValidateTokenAsync(int userId)
  {
    var userToken = await _tokenService.GetUserTokenAsync(userId);

    if (userToken == null)
    {
      return false;
    }

    // If token is expired, try to refresh it
    if (userToken.ExpiresAt <= DateTime.UtcNow)
    {
      try
      {
        var refreshResponse = await RefreshTokenAsync(userToken.RefreshToken);
        await _tokenService.UpdateTokenAsync(
            userToken,
            refreshResponse.AccessToken,
            refreshResponse.RefreshToken ?? userToken.RefreshToken,
            refreshResponse.ExpiresIn
        );
        return true;
      }
      catch (Exception)
      {
        return false;
      }
    }

    return true;
  }

  public async Task<SpotifyAuthResponse> RefreshTokenAsync(string refreshToken)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, _spotifySettings.TokenUrl);

    var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "refresh_token", refreshToken },
            { "client_id", _spotifySettings.ClientId },
            { "client_secret", _spotifySettings.ClientSecret }
        });

    request.Content = content;

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    var tokenResponse = JsonSerializer.Deserialize<SpotifyAuthResponse>(
        jsonResponse,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    if (tokenResponse == null)
    {
      throw new Exception("Failed to deserialize token response");
    }

    return tokenResponse;
  }

  private async Task<SpotifyAuthResponse> GetTokensFromCodeAsync(string code)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, _spotifySettings.TokenUrl);

    var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", _spotifySettings.RedirectUri },
            { "client_id", _spotifySettings.ClientId },
            { "client_secret", _spotifySettings.ClientSecret }
        });

    request.Content = content;

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    var tokenResponse = JsonSerializer.Deserialize<SpotifyAuthResponse>(
        jsonResponse,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    if (tokenResponse == null)
    {
      throw new Exception("Failed to deserialize token response");
    }

    return tokenResponse;
  }

  private async Task<SpotifyUserProfile> GetUserProfileAsync(string accessToken)
  {
    var request = new HttpRequestMessage(HttpMethod.Get, $"{_spotifySettings.ApiBaseUrl}/me");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var jsonResponse = await response.Content.ReadAsStringAsync();
    var userProfile = JsonSerializer.Deserialize<SpotifyUserProfile>(
        jsonResponse,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    if (userProfile == null)
    {
      throw new Exception("Failed to deserialize user profile");
    }

    return userProfile;
  }
}
