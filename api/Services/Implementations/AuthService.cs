using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RadioWash.Api.Configuration;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SupabaseSettings _supabaseSettings;
    private readonly RadioWashDbContext _dbContext;
    private readonly ILogger<AuthService> _logger;
    private readonly IMusicServiceAuthService _musicServiceAuthService;

    public AuthService(
        IHttpClientFactory httpClientFactory,
        IOptions<SupabaseSettings> supabaseSettings,
        RadioWashDbContext dbContext,
        ILogger<AuthService> logger,
        IMusicServiceAuthService musicServiceAuthService)
    {
        _httpClientFactory = httpClientFactory;
        _supabaseSettings = supabaseSettings.Value;
        _dbContext = dbContext;
        _logger = logger;
        _musicServiceAuthService = musicServiceAuthService;
    }

    public async Task<AuthResult> SignUpAsync(string email, string password, string displayName)
    {
        try
        {
            var supabaseResponse = await CallSupabaseAuthAsync("signup", new
            {
                email,
                password,
                data = new { display_name = displayName }
            });

            if (supabaseResponse == null)
            {
                return new AuthResult { Success = false, ErrorMessage = "Failed to create user" };
            }

            // User is automatically created in local database via trigger
            // Wait a moment for the trigger to complete, then fetch the user
            await Task.Delay(100);
            
            var supabaseUserId = Guid.Parse(supabaseResponse.User.Id);
            var user = await GetOrCreateLocalUserAsync(supabaseUserId, email, displayName);

            var userDto = new UserDto
            {
                Id = user.Id,
                SupabaseUserId = user.SupabaseUserId,
                Email = user.Email,
                DisplayName = user.DisplayName,
                ProfileImageUrl = null
            };

            // Check if user needs to set up music services
            var requiresMusicServiceSetup = await CheckRequiresMusicServiceSetupAsync(user.SupabaseUserId);

            return new AuthResult
            {
                Success = true,
                Token = supabaseResponse.AccessToken,
                User = userDto,
                RequiresMusicServiceSetup = requiresMusicServiceSetup
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user sign up");
            return new AuthResult { Success = false, ErrorMessage = "Sign up failed" };
        }
    }

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        try
        {
            var supabaseResponse = await CallSupabaseAuthAsync("token?grant_type=password", new
            {
                email,
                password
            });

            if (supabaseResponse == null)
            {
                return new AuthResult { Success = false, ErrorMessage = "Invalid credentials" };
            }

            var supabaseUserId = Guid.Parse(supabaseResponse.User.Id);
            var user = await GetOrCreateLocalUserAsync(supabaseUserId, email, supabaseResponse.User.Email);

            var userDto = new UserDto
            {
                Id = user.Id,
                SupabaseUserId = user.SupabaseUserId,
                Email = user.Email,
                DisplayName = user.DisplayName,
                ProfileImageUrl = null
            };

            // Check if user needs to set up music services
            var requiresMusicServiceSetup = await CheckRequiresMusicServiceSetupAsync(user.SupabaseUserId);

            return new AuthResult
            {
                Success = true,
                Token = supabaseResponse.AccessToken,
                User = userDto,
                RequiresMusicServiceSetup = requiresMusicServiceSetup
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user sign in");
            return new AuthResult { Success = false, ErrorMessage = "Sign in failed" };
        }
    }

    public async Task SignOutAsync()
    {
        try
        {
            // Call Supabase logout endpoint
            await CallSupabaseAuthAsync("logout", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sign out");
            // Don't throw - logout should be best effort
        }
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var supabaseResponse = await CallSupabaseAuthAsync("token?grant_type=refresh_token", new
            {
                refresh_token = refreshToken
            });

            if (supabaseResponse == null)
            {
                return new AuthResult { Success = false, ErrorMessage = "Invalid refresh token" };
            }

            return new AuthResult
            {
                Success = true,
                Token = supabaseResponse.AccessToken
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return new AuthResult { Success = false, ErrorMessage = "Token refresh failed" };
        }
    }

    public async Task<UserDto?> GetUserBySupabaseIdAsync(Guid supabaseUserId)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
        {
            return null;
        }

        return new UserDto
        {
            Id = user.Id,
            SupabaseUserId = user.SupabaseUserId,
            SpotifyId = user.SpotifyId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            ProfileImageUrl = null
        };
    }

    private async Task<User> GetOrCreateLocalUserAsync(Guid supabaseUserId, string email, string? displayName)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
        {
            user = new User
            {
                SupabaseUserId = supabaseUserId,
                Email = email,
                DisplayName = displayName ?? email.Split('@')[0]
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            // Update user information if needed
            if (user.Email != email || (displayName != null && user.DisplayName != displayName))
            {
                user.Email = email;
                if (displayName != null)
                {
                    user.DisplayName = displayName;
                }
                user.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
        }

        return user;
    }

    /// <summary>
    /// Checks if a user needs to set up music services by verifying they have at least one active, non-expired service
    /// </summary>
    private async Task<bool> CheckRequiresMusicServiceSetupAsync(Guid supabaseUserId)
    {
        try
        {
            var connectedServices = await _musicServiceAuthService.GetConnectedServicesAsync(supabaseUserId);
            
            // Check if user has any valid (active and non-expired) music services
            var hasValidService = connectedServices.Any(service => 
                service.IsActive && service.ExpiresAt > DateTime.UtcNow);
            
            // Return true if setup is required (no valid services)
            return !hasValidService;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking music service setup requirement for user {UserId}", supabaseUserId);
            // Default to requiring setup if we can't determine status
            return true;
        }
    }

    private async Task<SupabaseAuthResponse?> CallSupabaseAuthAsync(string endpoint, object? data)
    {
        using var client = _httpClientFactory.CreateClient();
        
        var url = $"{_supabaseSettings.Url}/auth/v1/{endpoint}";
        client.DefaultRequestHeaders.Add("apikey", _supabaseSettings.AnonKey);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_supabaseSettings.ServiceRoleKey}");

        HttpResponseMessage response;
        if (data != null)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower 
            });
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            response = await client.PostAsync(url, content);
        }
        else
        {
            response = await client.PostAsync(url, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Supabase auth error: {StatusCode} - {Content}", response.StatusCode, errorContent);
            return null;
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SupabaseAuthResponse>(responseContent, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower 
        });
    }

    private class SupabaseAuthResponse
    {
        public string AccessToken { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
        public int ExpiresIn { get; set; }
        public SupabaseUser User { get; set; } = null!;
    }

    private class SupabaseUser
    {
        public string Id { get; set; } = null!;
        public string Email { get; set; } = null!;
        public Dictionary<string, object>? UserMetadata { get; set; }
    }
}