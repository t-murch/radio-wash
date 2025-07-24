using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Service for managing music service OAuth tokens with encryption, validation, and refresh
/// </summary>
public interface IMusicTokenService
{
    /// <summary>
    /// Stores or updates encrypted tokens for a user and provider
    /// </summary>
    Task<UserMusicToken> StoreTokensAsync(int userId, string provider, string accessToken, 
        string? refreshToken, int expiresInSeconds, string[]? scopes = null, object? metadata = null);

    /// <summary>
    /// Retrieves decrypted access token for a user and provider
    /// Automatically refreshes if expired and refresh token is available
    /// </summary>
    Task<string> GetValidAccessTokenAsync(int userId, string provider);

    /// <summary>
    /// Gets token information without decrypting the actual tokens
    /// </summary>
    Task<UserMusicToken?> GetTokenInfoAsync(int userId, string provider);

    /// <summary>
    /// Checks if user has valid tokens for a provider
    /// </summary>
    Task<bool> HasValidTokensAsync(int userId, string provider);

    /// <summary>
    /// Refreshes expired tokens using refresh token
    /// </summary>
    Task<bool> RefreshTokensAsync(int userId, string provider);

    /// <summary>
    /// Revokes and removes all tokens for a user and provider
    /// </summary>
    Task RevokeTokensAsync(int userId, string provider);

    /// <summary>
    /// Validates that user has required scopes for a provider
    /// </summary>
    Task<bool> HasRequiredScopesAsync(int userId, string provider, string[] requiredScopes);
}