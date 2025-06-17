using RadioWash.Api.Models.DTO;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Defines the contract for authentication services.
/// </summary>
public interface IAuthService
{
  /// <summary>
  /// Generates the Spotify authorization URL with a given state for CSRF protection.
  /// </summary>
  /// <param name="state">A unique string to maintain state between the request and the callback.</param>
  /// <returns>The full Spotify authorization URL.</returns>
  string GenerateAuthUrl(string state);

  /// <summary>
  /// Handles the OAuth callback from Spotify. It exchanges the authorization code
  /// for tokens, gets or creates a user, and generates a JWT.
  /// </summary>
  /// <param name="code">The authorization code provided by Spotify.</param>
  /// <returns>An <see cref="AuthResponseDto"/> containing the JWT and user information.</returns>
  Task<AuthResponseDto> HandleCallbackAsync(string code);

  /// <summary>
  /// Retrieves a user's profile by their internal database ID.
  /// </summary>
  /// <param name="userId">The user's unique identifier.</param>
  /// <returns>A <see cref="UserDto"/> for the specified user, or null if not found.</returns>
  Task<UserDto?> GetUserByIdAsync(int userId);
}
