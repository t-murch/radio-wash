using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RadioWash.Api.Configuration;
using RadioWash.Api.Controllers;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class MusicServiceAuthService : IMusicServiceAuthService
{
    private readonly RadioWashDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SpotifySettings _spotifySettings;
    private readonly ILogger<MusicServiceAuthService> _logger;

    public MusicServiceAuthService(
        RadioWashDbContext context,
        IHttpClientFactory httpClientFactory,
        IOptions<SpotifySettings> spotifySettings,
        ILogger<MusicServiceAuthService> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _spotifySettings = spotifySettings.Value;
        _logger = logger;
    }

    public async Task<IEnumerable<UserMusicService>> GetConnectedServicesAsync(Guid supabaseUserId)
    {
        var user = await GetUserBySupabaseIdAsync(supabaseUserId);
        if (user == null) return Enumerable.Empty<UserMusicService>();

        return await _context.UserMusicServices
            .Where(ums => ums.UserId == user.Id && ums.IsActive)
            .ToListAsync();
    }

    public string GenerateSpotifyAuthUrl(string state)
    {
        var scopes = "user-read-email user-read-private playlist-read-private playlist-modify-private playlist-modify-public";
        var redirectUri = $"{_spotifySettings.RedirectUri}";
        
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = _spotifySettings.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = scopes,
            ["state"] = state
        };

        var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
        return $"https://accounts.spotify.com/authorize?{queryString}";
    }

    public async Task HandleSpotifyCallbackAsync(Guid supabaseUserId, string code)
    {
        var user = await GetUserBySupabaseIdAsync(supabaseUserId);
        if (user == null) throw new InvalidOperationException("User not found");

        var tokenResponse = await ExchangeSpotifyCodeForTokenAsync(code);
        var spotifyUser = await GetSpotifyUserInfoAsync(tokenResponse.AccessToken);

        var existingService = await _context.UserMusicServices
            .FirstOrDefaultAsync(ums => ums.UserId == user.Id && ums.ServiceType == MusicServiceType.Spotify);

        if (existingService != null)
        {
            existingService.ServiceUserId = spotifyUser.Id;
            existingService.AccessToken = tokenResponse.AccessToken;
            existingService.RefreshToken = tokenResponse.RefreshToken;
            existingService.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            existingService.IsActive = true;
        }
        else
        {
            var newService = new UserMusicService
            {
                UserId = user.Id,
                ServiceType = MusicServiceType.Spotify,
                ServiceUserId = spotifyUser.Id,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                IsActive = true
            };
            _context.UserMusicServices.Add(newService);
        }

        await _context.SaveChangesAsync();
    }

    public string GenerateAppleMusicAuthUrl(string state)
    {
        // Apple Music implementation would be more complex, requiring JWT tokens
        // This is a placeholder implementation
        throw new NotImplementedException("Apple Music authentication not yet implemented");
    }

    public Task HandleAppleMusicCallbackAsync(Guid supabaseUserId, string code)
    {
        // Apple Music implementation placeholder
        throw new NotImplementedException("Apple Music authentication not yet implemented");
    }

    public async Task DisconnectServiceAsync(Guid supabaseUserId, MusicServiceType serviceType)
    {
        var user = await GetUserBySupabaseIdAsync(supabaseUserId);
        if (user == null) return;

        var service = await _context.UserMusicServices
            .FirstOrDefaultAsync(ums => ums.UserId == user.Id && ums.ServiceType == serviceType);

        if (service != null)
        {
            service.IsActive = false;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<string?> GetValidTokenAsync(Guid supabaseUserId, MusicServiceType serviceType)
    {
        var user = await GetUserBySupabaseIdAsync(supabaseUserId);
        if (user == null) return null;

        var service = await _context.UserMusicServices
            .FirstOrDefaultAsync(ums => ums.UserId == user.Id && ums.ServiceType == serviceType && ums.IsActive);

        if (service == null) return null;

        // Check if token is expired (with 5 minute buffer)
        if (service.ExpiresAt <= DateTime.UtcNow.AddMinutes(5))
        {
            if (serviceType == MusicServiceType.Spotify && !string.IsNullOrEmpty(service.RefreshToken))
            {
                await RefreshSpotifyTokenAsync(service);
            }
            else
            {
                return null;
            }
        }

        return service.AccessToken;
    }

    private async Task<User?> GetUserBySupabaseIdAsync(Guid supabaseUserId)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);
    }

    private async Task<SpotifyTokenResponse> ExchangeSpotifyCodeForTokenAsync(string code)
    {
        using var client = _httpClientFactory.CreateClient();
        
        var requestData = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _spotifySettings.RedirectUri,
            ["client_id"] = _spotifySettings.ClientId,
            ["client_secret"] = _spotifySettings.ClientSecret
        };

        var requestContent = new FormUrlEncodedContent(requestData);
        var response = await client.PostAsync("https://accounts.spotify.com/api/token", requestContent);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to exchange code for token: {errorContent}");
        }

        var jsonContent = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<SpotifyTokenResponse>(jsonContent, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower 
        });

        return tokenResponse ?? throw new InvalidOperationException("Invalid token response");
    }

    private async Task<SpotifyUserInfo> GetSpotifyUserInfoAsync(string accessToken)
    {
        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("https://api.spotify.com/v1/me");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Failed to get Spotify user info");
        }

        var jsonContent = await response.Content.ReadAsStringAsync();
        var userInfo = JsonSerializer.Deserialize<SpotifyUserInfo>(jsonContent, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower 
        });

        return userInfo ?? throw new InvalidOperationException("Invalid user info response");
    }

    private async Task RefreshSpotifyTokenAsync(UserMusicService service)
    {
        using var client = _httpClientFactory.CreateClient();
        
        var requestData = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = service.RefreshToken!,
            ["client_id"] = _spotifySettings.ClientId,
            ["client_secret"] = _spotifySettings.ClientSecret
        };

        var requestContent = new FormUrlEncodedContent(requestData);
        var response = await client.PostAsync("https://accounts.spotify.com/api/token", requestContent);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to refresh Spotify token for service {ServiceId}", service.Id);
            service.IsActive = false;
            await _context.SaveChangesAsync();
            return;
        }

        var jsonContent = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<SpotifyTokenResponse>(jsonContent, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower 
        });

        if (tokenResponse != null)
        {
            service.AccessToken = tokenResponse.AccessToken;
            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                service.RefreshToken = tokenResponse.RefreshToken;
            }
            service.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            await _context.SaveChangesAsync();
        }
    }

    private class SpotifyTokenResponse
    {
        public string AccessToken { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
        public int ExpiresIn { get; set; }
    }

    private class SpotifyUserInfo
    {
        public string Id { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
    }
}