using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using RadioWash.Api.Configuration;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Tests.Unit.TestHelpers;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Tests for <see cref="AppleDeveloperTokenProvider"/> — the singleton that signs and caches
/// the ES256 developer JWT used against the Apple Music API and by MusicKit JS. Contracts:
/// the token carries Apple's required claims (iss = team id, kid header = key id, ES256 alg)
/// and verifies against the configured key; tokens are cached until near expiry and then
/// regenerated; all three key-material sources load; missing configuration fails at first
/// use with an actionable message rather than at startup.
/// </summary>
public class AppleDeveloperTokenProviderTests : IDisposable
{
  private const string TeamId = "TEAM123456";
  private const string KeyId = "KEY9876543";

  private readonly ECDsa _key;
  private readonly string _privateKeyPem;
  private readonly TestDateTimeProvider _dateTimeProvider = new();
  private readonly Mock<ILogger<AppleDeveloperTokenProvider>> _logger = new();

  public AppleDeveloperTokenProviderTests()
  {
    _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    _privateKeyPem = _key.ExportPkcs8PrivateKeyPem();
  }

  private AppleDeveloperTokenProvider CreateProvider(Action<AppleMusicSettings>? configure = null)
  {
    var settings = new AppleMusicSettings
    {
      TeamId = TeamId,
      KeyId = KeyId,
      PrivateKey = _privateKeyPem
    };
    configure?.Invoke(settings);
    return new AppleDeveloperTokenProvider(Options.Create(settings), _dateTimeProvider, _logger.Object);
  }

  [Fact]
  public async Task GetDeveloperTokenAsync_ProducesEs256JwtWithAppleRequiredClaims()
  {
    _dateTimeProvider.SetUtcNow(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    using var provider = CreateProvider();

    var token = await provider.GetDeveloperTokenAsync();
    var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

    Assert.Equal("ES256", jwt.Header.Alg);
    Assert.Equal(KeyId, jwt.Header.Kid);
    Assert.Equal(TeamId, jwt.Issuer);
    // Default lifetime is 150 days; exp is expressed in Unix seconds.
    Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(150), jwt.ValidTo);
    Assert.True(jwt.ValidFrom < _dateTimeProvider.UtcNow, "nbf should be backdated for clock skew");
  }

  [Fact]
  public async Task GetDeveloperTokenAsync_SignatureVerifiesAgainstConfiguredKey()
  {
    using var provider = CreateProvider();
    var token = await provider.GetDeveloperTokenAsync();

    using var publicKey = ECDsa.Create();
    publicKey.ImportSubjectPublicKeyInfo(_key.ExportSubjectPublicKeyInfo(), out _);

    var handler = new JwtSecurityTokenHandler();
    // ValidateToken throws when the signature, issuer, or lifetime is invalid.
    handler.ValidateToken(token, new TokenValidationParameters
    {
      ValidIssuer = TeamId,
      ValidateAudience = false,
      IssuerSigningKey = new ECDsaSecurityKey(publicKey)
    }, out var validated);

    Assert.Equal("ES256", ((JwtSecurityToken)validated).Header.Alg);
  }

  [Fact]
  public async Task GetDeveloperTokenAsync_ReturnsCachedTokenOnSubsequentCalls()
  {
    using var provider = CreateProvider();

    var first = await provider.GetDeveloperTokenAsync();
    _dateTimeProvider.AdvanceTime(TimeSpan.FromDays(30));
    var second = await provider.GetDeveloperTokenAsync();

    Assert.Equal(first, second);
  }

  [Fact]
  public async Task GetDeveloperTokenAsync_RegeneratesWhenNearExpiry()
  {
    // Whole-second clock: the JWT exp claim is Unix seconds, so fractional ticks would
    // make the equality assertion below flaky.
    _dateTimeProvider.SetUtcNow(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    using var provider = CreateProvider();

    var first = await provider.GetDeveloperTokenAsync();
    // Cross into the regeneration window (24h before the 150-day expiry).
    _dateTimeProvider.AdvanceTime(TimeSpan.FromDays(150) - TimeSpan.FromHours(12));
    var second = await provider.GetDeveloperTokenAsync();

    Assert.NotEqual(first, second);
    var jwt = new JwtSecurityTokenHandler().ReadJwtToken(second);
    Assert.Equal(_dateTimeProvider.UtcNow.AddDays(150), jwt.ValidTo);
  }

  [Fact]
  public async Task GetDeveloperTokenAsync_LoadsKeyFromBase64()
  {
    var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_privateKeyPem));
    using var provider = CreateProvider(s =>
    {
      s.PrivateKey = null;
      s.PrivateKeyBase64 = base64;
    });

    var token = await provider.GetDeveloperTokenAsync();
    Assert.Equal(KeyId, new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid);
  }

  [Fact]
  public async Task GetDeveloperTokenAsync_LoadsKeyFromPath()
  {
    var path = Path.Combine(Path.GetTempPath(), $"apple-musickit-test-{Guid.NewGuid():N}.p8");
    await File.WriteAllTextAsync(path, _privateKeyPem);
    try
    {
      using var provider = CreateProvider(s =>
      {
        s.PrivateKey = null;
        s.PrivateKeyPath = path;
      });

      var token = await provider.GetDeveloperTokenAsync();
      Assert.Equal(KeyId, new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid);
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Fact]
  public async Task GetDeveloperTokenAsync_WithoutConfiguration_ThrowsActionableError()
  {
    using var provider = CreateProvider(s =>
    {
      s.TeamId = null!;
      s.KeyId = null!;
      s.PrivateKey = null;
    });

    Assert.False(provider.IsConfigured);
    var ex = await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await provider.GetDeveloperTokenAsync());
    Assert.Contains("AppleMusic:TeamId", ex.Message);
  }

  [Fact]
  public async Task GetDeveloperTokenAsync_WithMalformedKey_ThrowsActionableError()
  {
    using var provider = CreateProvider(s => s.PrivateKey = "not-a-pem-key");

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await provider.GetDeveloperTokenAsync());
    Assert.Contains("private key", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  // Every mis-configuration must surface as InvalidOperationException — the devtoken
  // endpoint maps exactly that to 503 apple_music_not_configured. A raw FormatException or
  // FileNotFoundException would escape as an unexplained 500 instead.

  [Fact]
  public async Task GetDeveloperTokenAsync_WithInvalidBase64_ThrowsActionableError()
  {
    using var provider = CreateProvider(s =>
    {
      s.PrivateKey = null;
      s.PrivateKeyBase64 = "%%% definitely not base64 %%%";
    });

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await provider.GetDeveloperTokenAsync());
    Assert.Contains("PrivateKeyBase64", ex.Message);
    Assert.IsType<FormatException>(ex.InnerException);
  }

  [Fact]
  public async Task GetDeveloperTokenAsync_WithMissingKeyFile_ThrowsActionableError()
  {
    var missingPath = Path.Combine(Path.GetTempPath(), $"apple-musickit-missing-{Guid.NewGuid():N}.p8");
    using var provider = CreateProvider(s =>
    {
      s.PrivateKey = null;
      s.PrivateKeyPath = missingPath;
    });

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await provider.GetDeveloperTokenAsync());
    Assert.Contains("PrivateKeyPath", ex.Message);
    Assert.Contains(missingPath, ex.Message);
  }

  [Fact]
  public void IsConfigured_TrueWhenAnyKeySourcePresent()
  {
    using var provider = CreateProvider();
    Assert.True(provider.IsConfigured);
  }

  public void Dispose()
  {
    _key.Dispose();
  }
}
