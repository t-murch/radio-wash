using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;
using SpotifyAPI.Web;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Comprehensive music token management service with encryption, validation, and refresh capabilities
/// </summary>
public class MusicTokenService : IMusicTokenService
{
    private readonly RadioWashDbContext _dbContext;
    private readonly ITokenEncryptionService _encryptionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MusicTokenService> _logger;
    private readonly HttpClient _httpClient;

    public MusicTokenService(
        RadioWashDbContext dbContext,
        ITokenEncryptionService encryptionService,
        IConfiguration configuration,
        ILogger<MusicTokenService> logger,
        HttpClient httpClient)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<UserMusicToken> StoreTokensAsync(int userId, string provider, string accessToken, 
        string? refreshToken, int expiresInSeconds, string[]? scopes = null, object? metadata = null)
    {
        var existingToken = await _dbContext.UserMusicTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == provider);

        var encryptedAccessToken = _encryptionService.EncryptToken(accessToken);
        var encryptedRefreshToken = refreshToken != null ? _encryptionService.EncryptToken(refreshToken) : null;
        var scopesJson = scopes != null ? JsonSerializer.Serialize(scopes) : null;
        var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : null;

        if (existingToken != null)
        {
            existingToken.EncryptedAccessToken = encryptedAccessToken;
            existingToken.EncryptedRefreshToken = encryptedRefreshToken;
            existingToken.ExpiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds);
            existingToken.Scopes = scopesJson;
            existingToken.ProviderMetadata = metadataJson;
            existingToken.UpdatedAt = DateTime.UtcNow;
            
            _logger.LogInformation("Updated tokens for user {UserId} provider {Provider}", userId, provider);
        }
        else
        {
            existingToken = new UserMusicToken
            {
                UserId = userId,
                Provider = provider,
                EncryptedAccessToken = encryptedAccessToken,
                EncryptedRefreshToken = encryptedRefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds),
                Scopes = scopesJson,
                ProviderMetadata = metadataJson,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            _dbContext.UserMusicTokens.Add(existingToken);
            _logger.LogInformation("Created new tokens for user {UserId} provider {Provider}", userId, provider);
        }

        await _dbContext.SaveChangesAsync();
        return existingToken;
    }

    public async Task<string> GetValidAccessTokenAsync(int userId, string provider)
    {
        var tokenRecord = await GetTokenInfoAsync(userId, provider);
        if (tokenRecord == null)
        {
            throw new UnauthorizedAccessException($"No tokens found for user {userId} provider {provider}");
        }

        // Check if token is expired
        if (DateTime.UtcNow >= tokenRecord.ExpiresAt.AddMinutes(-5)) // Refresh 5 minutes early
        {
            _logger.LogInformation("Token expired for user {UserId} provider {Provider}, attempting refresh", userId, provider);
            
            var refreshed = await RefreshTokensAsync(userId, provider);
            if (!refreshed)
            {
                throw new UnauthorizedAccessException($"Token expired and refresh failed for user {userId} provider {provider}");
            }
            
            // Reload the updated token
            tokenRecord = await GetTokenInfoAsync(userId, provider);
            if (tokenRecord == null)
            {
                throw new InvalidOperationException("Token disappeared after refresh");
            }
        }

        return _encryptionService.DecryptToken(tokenRecord.EncryptedAccessToken);
    }

    public async Task<UserMusicToken?> GetTokenInfoAsync(int userId, string provider)
    {
        return await _dbContext.UserMusicTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == provider);
    }

    public async Task<bool> HasValidTokensAsync(int userId, string provider)
    {
        var tokenRecord = await GetTokenInfoAsync(userId, provider);
        if (tokenRecord == null)
        {
            return false;
        }

        // Token is valid if not expired or we have a refresh token
        return DateTime.UtcNow < tokenRecord.ExpiresAt || !string.IsNullOrEmpty(tokenRecord.EncryptedRefreshToken);
    }

    public async Task<bool> RefreshTokensAsync(int userId, string provider)
    {
        var tokenRecord = await GetTokenInfoAsync(userId, provider);
        if (tokenRecord?.EncryptedRefreshToken == null)
        {
            _logger.LogWarning("No refresh token available for user {UserId} provider {Provider}", userId, provider);
            return false;
        }

        try
        {
            if (provider.ToLower() == "spotify")
            {
                return await RefreshSpotifyTokenAsync(tokenRecord);
            }
            
            _logger.LogWarning("Refresh not implemented for provider {Provider}", provider);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh tokens for user {UserId} provider {Provider}", userId, provider);
            return false;
        }
    }

    public async Task RevokeTokensAsync(int userId, string provider)
    {
        var tokenRecord = await GetTokenInfoAsync(userId, provider);
        if (tokenRecord != null)
        {
            _dbContext.UserMusicTokens.Remove(tokenRecord);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Revoked tokens for user {UserId} provider {Provider}", userId, provider);
        }
    }

    public async Task<bool> HasRequiredScopesAsync(int userId, string provider, string[] requiredScopes)
    {
        var tokenRecord = await GetTokenInfoAsync(userId, provider);
        if (tokenRecord?.Scopes == null)
        {
            return false;
        }

        try
        {
            var grantedScopes = JsonSerializer.Deserialize<string[]>(tokenRecord.Scopes) ?? Array.Empty<string>();
            return requiredScopes.All(required => grantedScopes.Contains(required));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse scopes for user {UserId} provider {Provider}", userId, provider);
            return false;
        }
    }

    private async Task<bool> RefreshSpotifyTokenAsync(UserMusicToken tokenRecord)
    {
        var clientId = _configuration["Spotify:ClientId"];
        var clientSecret = _configuration["Spotify:ClientSecret"];
        var refreshToken = _encryptionService.DecryptToken(tokenRecord.EncryptedRefreshToken!);

        try
        {
            var request = new AuthorizationCodeRefreshRequest(clientId!, clientSecret!, refreshToken);
            var response = await new OAuthClient().RequestToken(request);

            if (response.AccessToken != null)
            {
                tokenRecord.EncryptedAccessToken = _encryptionService.EncryptToken(response.AccessToken);
                tokenRecord.ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn);
                
                // Spotify may provide a new refresh token
                if (!string.IsNullOrEmpty(response.RefreshToken))
                {
                    tokenRecord.EncryptedRefreshToken = _encryptionService.EncryptToken(response.RefreshToken);
                }
                
                tokenRecord.MarkRefreshSuccess();
                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation("Successfully refreshed Spotify token for user {UserId}", tokenRecord.UserId);
                return true;
            }
            else
            {
                tokenRecord.MarkRefreshFailure();
                await _dbContext.SaveChangesAsync();
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh Spotify token for user {UserId}", tokenRecord.UserId);
            tokenRecord.MarkRefreshFailure();
            await _dbContext.SaveChangesAsync();
            return false;
        }
    }
}