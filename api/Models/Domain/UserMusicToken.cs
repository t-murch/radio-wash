using System.ComponentModel.DataAnnotations;

namespace RadioWash.Api.Models.Domain;

/// <summary>
/// Stores encrypted OAuth tokens for music service providers (Spotify, Apple Music, etc.)
/// Implements secure token storage with encryption, validation, and automatic refresh capabilities
/// </summary>
public class UserMusicToken
{
  public int Id { get; set; }

  [Required]
  public int UserId { get; set; }

  [Required]
  [MaxLength(50)]
  public string Provider { get; set; } = null!; // "spotify", "apple_music", etc.

  /// <summary>
  /// Encrypted access token using ASP.NET Core Data Protection API
  /// Never store tokens in plaintext for security compliance
  /// </summary>
  [Required]
  public string EncryptedAccessToken { get; set; } = null!;

  /// <summary>
  /// Encrypted refresh token for automatic token renewal
  /// May be null for providers that don't support refresh tokens
  /// </summary>
  public string? EncryptedRefreshToken { get; set; }

  /// <summary>
  /// UTC timestamp when the access token expires
  /// Used for proactive token refresh before expiration
  /// </summary>
  [Required]
  public DateTime ExpiresAt { get; set; }

  /// <summary>
  /// JSON array of OAuth scopes granted (e.g., ["playlist-read-private", "playlist-modify-public"])
  /// Used for validating permission requirements before API calls
  /// </summary>
  [MaxLength(2000)]
  public string? Scopes { get; set; }

  /// <summary>
  /// Provider-specific metadata stored as JSON (user profile info, quotas, etc.)
  /// Useful for caching provider details and rate limiting
  /// </summary>
  [MaxLength(4000)]
  public string? ProviderMetadata { get; set; }

  /// <summary>
  /// Number of consecutive refresh failures
  /// Used to implement exponential backoff and disable automatic refresh after multiple failures
  /// </summary>
  public int RefreshFailureCount { get; set; } = 0;

  /// <summary>
  /// Timestamp of last successful token refresh
  /// Used for monitoring token health and refresh patterns
  /// </summary>
  public DateTime? LastRefreshAt { get; set; }

  /// <summary>
  /// Flag indicating if this token has been revoked by user or provider
  /// Prevents unnecessary API calls for known invalid tokens
  /// </summary>
  public bool IsRevoked { get; set; } = false;

  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  public User User { get; set; } = null!;

  /// <summary>
  /// Determines if token is likely expired based on ExpiresAt timestamp
  /// Includes 5-minute buffer for clock skew and proactive refresh
  /// </summary>
  public bool IsExpired => DateTime.UtcNow >= ExpiresAt.AddMinutes(-5);

  /// <summary>
  /// Determines if token can be refreshed (has refresh token and not repeatedly failed)
  /// </summary>
  public bool CanRefresh => !string.IsNullOrEmpty(EncryptedRefreshToken) &&
                            RefreshFailureCount < 5 &&
                            !IsRevoked;

  /// <summary>
  /// Marks this token as successfully refreshed, resetting failure counters
  /// </summary>
  public void MarkRefreshSuccess()
  {
    RefreshFailureCount = 0;
    LastRefreshAt = DateTime.UtcNow;
    UpdatedAt = DateTime.UtcNow;
  }

  /// <summary>
  /// Increments refresh failure count for exponential backoff logic
  /// </summary>
  public void MarkRefreshFailure()
  {
    RefreshFailureCount++;
    UpdatedAt = DateTime.UtcNow;
  }
}
