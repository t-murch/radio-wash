using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RadioWash.Api.Configuration;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class AppleDeveloperTokenProvider : IAppleDeveloperTokenProvider, IDisposable
{
  // Regenerate well before expiry so a token handed to MusicKit JS stays valid for the
  // whole browser session that received it.
  private static readonly TimeSpan RegenerationWindow = TimeSpan.FromHours(24);
  // Backdate iat/nbf to tolerate clock skew between us and Apple.
  private static readonly TimeSpan ClockSkewBackdate = TimeSpan.FromMinutes(5);

  private readonly AppleMusicSettings _settings;
  private readonly IDateTimeProvider _dateTimeProvider;
  private readonly ILogger<AppleDeveloperTokenProvider> _logger;
  private readonly SemaphoreSlim _generationLock = new(1, 1);

  private sealed record CachedToken(string Value, DateTime ExpiresAt);
  private CachedToken? _cached;
  private ECDsa? _key;

  public AppleDeveloperTokenProvider(
      IOptions<AppleMusicSettings> settings,
      IDateTimeProvider dateTimeProvider,
      ILogger<AppleDeveloperTokenProvider> logger)
  {
    _settings = settings.Value;
    _dateTimeProvider = dateTimeProvider;
    _logger = logger;
  }

  public bool IsConfigured =>
      !string.IsNullOrWhiteSpace(_settings.TeamId) &&
      !string.IsNullOrWhiteSpace(_settings.KeyId) &&
      (!string.IsNullOrWhiteSpace(_settings.PrivateKey) ||
       !string.IsNullOrWhiteSpace(_settings.PrivateKeyBase64) ||
       !string.IsNullOrWhiteSpace(_settings.PrivateKeyPath));

  public async ValueTask<string> GetDeveloperTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
  {
    var cached = _cached;
    if (!forceRefresh && cached != null && _dateTimeProvider.UtcNow < cached.ExpiresAt - RegenerationWindow)
    {
      return cached.Value;
    }

    await _generationLock.WaitAsync(cancellationToken);
    try
    {
      var stale = cached;
      cached = _cached;
      // A waiter that queued behind a forced refresh is served by that refresh; only the
      // caller whose snapshot is still current regenerates again.
      var refreshedWhileWaiting = forceRefresh && !ReferenceEquals(stale, cached);
      if ((!forceRefresh || refreshedWhileWaiting) &&
          cached != null && _dateTimeProvider.UtcNow < cached.ExpiresAt - RegenerationWindow)
      {
        return cached.Value;
      }

      var generated = GenerateToken();
      _cached = generated;
      _logger.LogInformation(
        "Generated Apple Music developer token valid until {ExpiresAt:u}", generated.ExpiresAt);
      return generated.Value;
    }
    finally
    {
      _generationLock.Release();
    }
  }

  private CachedToken GenerateToken()
  {
    if (!IsConfigured)
    {
      throw new InvalidOperationException(
        "Apple Music is not configured. Set AppleMusic:TeamId, AppleMusic:KeyId and one of " +
        "AppleMusic:PrivateKey / AppleMusic:PrivateKeyBase64 / AppleMusic:PrivateKeyPath.");
    }

    _key ??= LoadPrivateKey();

    var now = _dateTimeProvider.UtcNow;
    var expiresAt = now.AddDays(_settings.DeveloperTokenLifetimeDays);
    var securityKey = new ECDsaSecurityKey(_key) { KeyId = _settings.KeyId };
    var descriptor = new SecurityTokenDescriptor
    {
      Issuer = _settings.TeamId,
      IssuedAt = now - ClockSkewBackdate,
      NotBefore = now - ClockSkewBackdate,
      Expires = expiresAt,
      SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256)
    };

    var handler = new JwtSecurityTokenHandler();
    return new CachedToken(handler.CreateEncodedJwt(descriptor), expiresAt);
  }

  private ECDsa LoadPrivateKey()
  {
    string pem;
    if (!string.IsNullOrWhiteSpace(_settings.PrivateKey))
    {
      pem = _settings.PrivateKey;
    }
    else if (!string.IsNullOrWhiteSpace(_settings.PrivateKeyBase64))
    {
      pem = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(_settings.PrivateKeyBase64));
    }
    else
    {
      pem = File.ReadAllText(_settings.PrivateKeyPath!);
    }

    var key = ECDsa.Create();
    try
    {
      key.ImportFromPem(pem);
    }
    catch (Exception ex) when (ex is ArgumentException or CryptographicException)
    {
      key.Dispose();
      throw new InvalidOperationException(
        "The configured Apple Music private key is not a valid PKCS#8 PEM (.p8) key.", ex);
    }
    return key;
  }

  public void Dispose()
  {
    _generationLock.Dispose();
    _key?.Dispose();
  }
}
