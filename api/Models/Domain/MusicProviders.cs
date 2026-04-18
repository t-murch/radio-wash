namespace RadioWash.Api.Models.Domain;

public static class MusicProviders
{
  public const string Spotify = "spotify";

  private static readonly Dictionary<string, string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
  {
    [Spotify] = Spotify
  };

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
