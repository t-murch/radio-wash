using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Per-provider strategy for refreshing a stored OAuth access token. Implementations own the
/// platform-specific token endpoint call and the success/failure bookkeeping on the
/// <see cref="UserMusicToken"/> record. MusicTokenService owns the orchestration concerns
/// (lookup, per-user lock, concurrent-refresh deduplication) and delegates the actual
/// dispatch here so adding Apple Music or YouTube Music doesn't require touching
/// MusicTokenService at all.
/// </summary>
public interface IMusicTokenRefresher
{
  /// <summary>
  /// Provider key used when resolving this refresher via keyed DI. Must match the value
  /// stored on <see cref="UserMusicToken.Provider"/> for the matching tokens (e.g., "spotify").
  /// </summary>
  string ProviderName { get; }

  /// <summary>
  /// Refreshes the given token record. Implementations are responsible for calling the
  /// provider's OAuth endpoint, decrypting the refresh token, encrypting and persisting the
  /// new access token, and calling <see cref="UserMusicToken.MarkRefreshSuccess"/> or
  /// <see cref="UserMusicToken.MarkRefreshFailure"/> as appropriate. Must not propagate
  /// exceptions — a failed refresh returns <c>false</c>.
  /// </summary>
  Task<bool> RefreshAsync(UserMusicToken token, CancellationToken cancellationToken);
}
