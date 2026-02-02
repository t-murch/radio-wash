using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace RadioWash.Api.Tests.Integration.TestHelpers;

/// <summary>
/// Helper class for generating JWT tokens for integration tests.
/// Supports both symmetric (HS256) and asymmetric (ES256) signing.
/// </summary>
public class JwtTestHelper : IDisposable
{
    private readonly ECDsa _ecdsaKey;
    private readonly string _keyId;

    public ECDsaSecurityKey SigningKey { get; }
    public ECDsaSecurityKey VerificationKey { get; }
    public string KeyId => _keyId;

    public JwtTestHelper()
    {
        _keyId = Guid.NewGuid().ToString();
        _ecdsaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        SigningKey = new ECDsaSecurityKey(_ecdsaKey) { KeyId = _keyId };
        VerificationKey = new ECDsaSecurityKey(ECDsa.Create(_ecdsaKey.ExportParameters(false))) { KeyId = _keyId };
    }

    /// <summary>
    /// Generates a valid ES256 JWT token for testing.
    /// </summary>
    public string GenerateToken(
        string issuer,
        string audience,
        Guid userId,
        string? email = null,
        TimeSpan? expiration = null,
        Dictionary<string, object>? additionalClaims = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var expires = now.Add(expiration ?? TimeSpan.FromHours(1));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        if (additionalClaims != null)
        {
            foreach (var claim in additionalClaims)
            {
                claims.Add(new Claim(claim.Key, claim.Value.ToString() ?? ""));
            }
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            NotBefore = now,
            IssuedAt = now,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.EcdsaSha256)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Generates an expired token for testing rejection of expired tokens.
    /// </summary>
    public string GenerateExpiredToken(string issuer, string audience, Guid userId)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow.AddHours(-2);
        var expires = now.AddHours(1); // Expired 1 hour ago

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            NotBefore = now,
            IssuedAt = now,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.EcdsaSha256)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Generates a token signed with a different key (for testing signature validation).
    /// </summary>
    public string GenerateTokenWithDifferentKey(string issuer, string audience, Guid userId)
    {
        using var differentKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signingKey = new ECDsaSecurityKey(differentKey) { KeyId = Guid.NewGuid().ToString() };

        var tokenHandler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = now.AddHours(1),
            NotBefore = now,
            IssuedAt = now,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Gets the JWKS (JSON Web Key Set) representation of the public key.
    /// </summary>
    public JsonWebKeySet GetJwks()
    {
        var parameters = _ecdsaKey.ExportParameters(false);

        var jwk = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
            Use = "sig",
            Kid = _keyId,
            Alg = SecurityAlgorithms.EcdsaSha256,
            Crv = "P-256",
            X = Base64UrlEncoder.Encode(parameters.Q.X!),
            Y = Base64UrlEncoder.Encode(parameters.Q.Y!)
        };
        jwk.KeyOps.Add("verify");

        var jwks = new JsonWebKeySet();
        jwks.Keys.Add(jwk);

        return jwks;
    }

    /// <summary>
    /// Gets the JWKS as a JSON string for serving from a mock endpoint.
    /// </summary>
    public string GetJwksJson()
    {
        var jwks = GetJwks();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            keys = jwks.Keys.Select(k => new
            {
                kty = k.Kty,
                use = k.Use,
                kid = k.Kid,
                alg = k.Alg,
                crv = k.Crv,
                x = k.X,
                y = k.Y,
                key_ops = new[] { "verify" }
            })
        });
    }

    public void Dispose()
    {
        _ecdsaKey.Dispose();
    }
}
