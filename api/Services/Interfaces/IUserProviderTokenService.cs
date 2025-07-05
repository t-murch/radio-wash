namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Defines a service for securely retrieving a user's OAuth provider tokens from Supabase.
/// </summary>
public interface IUserProviderTokenService
{
  /// <summary>
  /// Gets the access token for a specific provider (e.g., "spotify") for a given user.
  /// </summary>
  /// <param name="supabaseUserId">The user's unique ID from Supabase Auth.</param>
  /// <returns>The provider's access token.</returns>
  Task<string> GetProviderAccessTokenAsync(string supabaseUserId);
}
