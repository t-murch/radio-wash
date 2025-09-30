using Microsoft.AspNetCore.DataProtection;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Secure token encryption service using ASP.NET Core Data Protection API
/// Provides key management, rotation, and secure encryption for OAuth tokens
/// </summary>
public class TokenEncryptionService : ITokenEncryptionService
{
  private readonly IDataProtector _protector;
  private readonly ILogger<TokenEncryptionService> _logger;

  public TokenEncryptionService(IDataProtectionProvider dataProtectionProvider, ILogger<TokenEncryptionService> logger)
  {
    // Create a protector with a specific purpose string for token encryption
    _protector = dataProtectionProvider.CreateProtector("RadioWash.MusicTokens.v1");
    _logger = logger;
  }

  public string EncryptToken(string plaintext)
  {
    if (string.IsNullOrWhiteSpace(plaintext))
    {
      throw new ArgumentException("Token cannot be null or empty", nameof(plaintext));
    }

    try
    {
      return _protector.Protect(plaintext);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to encrypt token");
      throw new InvalidOperationException("Token encryption failed", ex);
    }
  }

  public string DecryptToken(string encryptedToken)
  {
    if (string.IsNullOrWhiteSpace(encryptedToken))
    {
      throw new ArgumentException("Encrypted token cannot be null or empty", nameof(encryptedToken));
    }

    try
    {
      return _protector.Unprotect(encryptedToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to decrypt token - may be corrupted or using old key");
      throw new InvalidOperationException("Token decryption failed", ex);
    }
  }

  public bool IsValidEncryptedToken(string encryptedToken)
  {
    if (string.IsNullOrWhiteSpace(encryptedToken))
    {
      return false;
    }

    try
    {
      _protector.Unprotect(encryptedToken);
      return true;
    }
    catch
    {
      return false;
    }
  }
}
