namespace RadioWash.Api.Models.DTO;

/// <summary>
/// Body for <c>POST /api/auth/tokens/{provider}</c>. Provider-neutral: OAuth happens entirely
/// in the frontend and the API only receives the already-obtained credentials.
/// </summary>
public class ProviderTokenRequest
{
  public string AccessToken { get; set; } = string.Empty;

  /// <summary>
  /// Optional: only providers with a refresh flow supply one. An Apple Music User Token has no
  /// refresh counterpart, so the Apple connect path posts this as null — a non-nullable type
  /// here would fail model validation before the request reached the controller.
  /// </summary>
  public string? RefreshToken { get; set; }
}
