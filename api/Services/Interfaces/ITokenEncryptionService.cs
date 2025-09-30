namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Service for encrypting and decrypting sensitive OAuth tokens
/// Uses ASP.NET Core Data Protection API for secure, key-managed encryption
/// </summary>
public interface ITokenEncryptionService
{
  /// <summary>
  /// Encrypts a plaintext token using Data Protection API
  /// </summary>
  /// <param name="plaintext">The token to encrypt</param>
  /// <returns>Encrypted token string</returns>
  string EncryptToken(string plaintext);

  /// <summary>
  /// Decrypts an encrypted token back to plaintext
  /// </summary>
  /// <param name="encryptedToken">The encrypted token</param>
  /// <returns>Decrypted plaintext token</returns>
  string DecryptToken(string encryptedToken);

  /// <summary>
  /// Safely validates if an encrypted token can be decrypted without throwing
  /// </summary>
  /// <param name="encryptedToken">The encrypted token to validate</param>
  /// <returns>True if token can be decrypted, false otherwise</returns>
  bool IsValidEncryptedToken(string encryptedToken);
}
