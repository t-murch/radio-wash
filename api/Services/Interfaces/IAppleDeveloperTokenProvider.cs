namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Supplies the Apple Music developer token: an ES256 JWT signed with the MusicKit
/// private key. The token authenticates the app (not a user) against the Apple Music
/// API and is also handed to MusicKit JS on the frontend to start user authorization.
/// </summary>
public interface IAppleDeveloperTokenProvider
{
  /// <summary>
  /// Whether the Apple Music credentials (team id, key id, private key) are configured.
  /// Deployments without Apple Music must still boot, so missing configuration only
  /// fails when a token is actually requested.
  /// </summary>
  bool IsConfigured { get; }

  /// <summary>
  /// Returns a cached developer token, generating a fresh one when none exists or the
  /// cached token is close to expiry. Pass <paramref name="forceRefresh"/> to discard the
  /// cache (e.g. after the API rejected the current token with 401).
  /// </summary>
  ValueTask<string> GetDeveloperTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}
