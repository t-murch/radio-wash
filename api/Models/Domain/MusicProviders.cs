namespace RadioWash.Api.Models.Domain;

public static class MusicProviders
{
  public const string Spotify = "spotify";
  public const string AppleMusic = "apple_music";

  private static readonly Dictionary<string, string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
  {
    [Spotify] = Spotify,
    [AppleMusic] = AppleMusic
  };

  /// <summary>
  /// Every supported canonical provider key. For callers that act across providers (e.g.
  /// revoking all connections on logout) so they can't silently miss one added later.
  /// </summary>
  public static IReadOnlyCollection<string> All { get; } = SupportedProviders.Values.Distinct().ToArray();

  // Providers whose credentials can be renewed without user interaction. Apple Music is
  // absent by design: a Music User Token has no refresh flow and no expiry signal, so its
  // stored ExpiresAt is an assumed lifetime rather than an authority.
  private static readonly HashSet<string> ProvidersWithTokenRefresh = new(StringComparer.OrdinalIgnoreCase)
  {
    Spotify
  };

  /// <summary>
  /// Whether the provider offers a token refresh flow. False means a stored expiry is a
  /// local assumption, so callers should defer to the provider's own authorization response
  /// instead of preemptively rejecting the token.
  /// </summary>
  public static bool SupportsTokenRefresh(string provider) =>
    TryNormalize(provider, out var normalized) && ProvidersWithTokenRefresh.Contains(normalized);

  public static bool TryNormalize(string provider, out string normalizedProvider)
  {
    if (!TryNormalizeKey(provider, out var normalizedKey))
    {
      normalizedProvider = string.Empty;
      return false;
    }

    return SupportedProviders.TryGetValue(normalizedKey, out normalizedProvider!);
  }

  public static string NormalizeOrThrow(string provider)
  {
    if (TryNormalize(provider, out var normalizedProvider))
    {
      return normalizedProvider;
    }

    throw new ArgumentException($"Provider '{provider}' is not supported.");
  }

  public static string NormalizeOrDefault(string? provider, string defaultProvider = Spotify)
  {
    return string.IsNullOrWhiteSpace(provider)
      ? defaultProvider
      : NormalizeOrThrow(provider);
  }

  public static string NormalizeKeyOrThrow(string provider)
  {
    if (TryNormalizeKey(provider, out var normalizedKey))
    {
      return normalizedKey;
    }

    throw new ArgumentException("Provider must be a non-empty string.", nameof(provider));
  }

  private static bool TryNormalizeKey(string provider, out string normalizedKey)
  {
    normalizedKey = string.Empty;

    if (string.IsNullOrWhiteSpace(provider))
    {
      return false;
    }

    normalizedKey = provider.Trim().ToLowerInvariant();
    return true;
  }
}
