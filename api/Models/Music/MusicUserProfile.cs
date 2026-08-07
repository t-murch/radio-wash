namespace RadioWash.Api.Models.Music;

/// <summary>
/// Provider-agnostic representation of the authenticated user's profile at a music service.
/// Used to obtain the platform-native user identifier needed when creating a playlist on that
/// platform (some require the user ID in the create-playlist URL).
/// </summary>
public record MusicUserProfile(
    string Id,
    string? DisplayName,
    string? Email);
