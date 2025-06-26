using Microsoft.AspNetCore.DataProtection;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class EncryptionService : IEncryptionService
{
    private readonly IDataProtector _protector;

    public EncryptionService(IDataProtectionProvider provider)
    {
        // The purpose string makes this protector unique.
        _protector = provider.CreateProtector("RadioWash.Spotify.v1");
    }

    public string Encrypt(string plaintext) => _protector.Protect(plaintext);
    
    public string Decrypt(string ciphertext) => _protector.Unprotect(ciphertext);
}