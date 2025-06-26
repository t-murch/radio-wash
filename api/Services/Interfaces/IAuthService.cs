using RadioWash.Api.Models.DTO;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Defines the contract for authentication services using Supabase.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Signs up a new user with Supabase authentication.
    /// </summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="password">The user's password.</param>
    /// <param name="displayName">The user's display name.</param>
    /// <returns>An <see cref="AuthResult"/> containing the result of the operation.</returns>
    Task<AuthResult> SignUpAsync(string email, string password, string displayName);

    /// <summary>
    /// Signs in a user with email and password using Supabase authentication.
    /// </summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="password">The user's password.</param>
    /// <returns>An <see cref="AuthResult"/> containing the result of the operation.</returns>
    Task<AuthResult> SignInAsync(string email, string password);

    /// <summary>
    /// Signs out the current user from Supabase.
    /// </summary>
    Task SignOutAsync();

    /// <summary>
    /// Refreshes an authentication token using a refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <returns>An <see cref="AuthResult"/> containing the new token.</returns>
    Task<AuthResult> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// Retrieves a user's profile by their Supabase user ID.
    /// </summary>
    /// <param name="supabaseUserId">The user's Supabase UUID.</param>
    /// <returns>A <see cref="UserDto"/> for the specified user, or null if not found.</returns>
    Task<UserDto?> GetUserBySupabaseIdAsync(Guid supabaseUserId);
}

/// <summary>
/// Represents the result of an authentication operation.
/// </summary>
public class AuthResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Token { get; set; }
    public UserDto? User { get; set; }
    
    /// <summary>
    /// Indicates whether the user needs to set up at least one music service
    /// before they can access the main application features.
    /// </summary>
    public bool RequiresMusicServiceSetup { get; set; }
}