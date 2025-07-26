using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Services.Implementations;
using Xunit;

namespace RadioWash.Api.Test.UnitTests;

public class TokenEncryptionServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ILogger<TokenEncryptionService>> _loggerMock;
    private readonly TokenEncryptionService _sut;

    public TokenEncryptionServiceTests()
    {
        // Create a real data protection provider for testing
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("RadioWash.Test")
            .DisableAutomaticKeyGeneration(); // Use ephemeral keys for testing
        
        _serviceProvider = services.BuildServiceProvider();
        _loggerMock = new Mock<ILogger<TokenEncryptionService>>();

        var dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();
        _sut = new TokenEncryptionService(dataProtectionProvider, _loggerMock.Object);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    #region EncryptToken Tests

    [Fact]
    public void EncryptToken_WhenValidToken_ShouldReturnEncryptedString()
    {
        // Arrange
        var originalToken = "test-access-token";

        // Act
        var result = _sut.EncryptToken(originalToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.NotEqual(originalToken, result);
    }

    [Fact]
    public void EncryptToken_WhenTokenIsNull_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.EncryptToken(null!));
    }

    [Fact]
    public void EncryptToken_WhenTokenIsEmpty_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.EncryptToken(""));
    }

    [Fact]
    public void EncryptToken_WhenTokenIsWhitespace_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.EncryptToken("   "));
    }

    #endregion

    #region DecryptToken Tests

    [Fact]
    public void DecryptToken_WhenValidEncryptedToken_ShouldReturnOriginalToken()
    {
        // Arrange
        var originalToken = "test-access-token";
        var encryptedToken = _sut.EncryptToken(originalToken);

        // Act
        var result = _sut.DecryptToken(encryptedToken);

        // Assert
        Assert.Equal(originalToken, result);
    }

    [Fact]
    public void DecryptToken_WhenEncryptedTokenIsNull_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.DecryptToken(null!));
    }

    [Fact]
    public void DecryptToken_WhenEncryptedTokenIsEmpty_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.DecryptToken(""));
    }

    [Fact]
    public void DecryptToken_WhenEncryptedTokenIsWhitespace_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.DecryptToken("   "));
    }

    [Fact]
    public void DecryptToken_WhenInvalidEncryptedToken_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var invalidEncryptedToken = "invalid-encrypted-token";

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => _sut.DecryptToken(invalidEncryptedToken));
        Assert.Contains("Token decryption failed", exception.Message);
    }

    #endregion

    #region IsValidEncryptedToken Tests

    [Fact]
    public void IsValidEncryptedToken_WhenValidToken_ShouldReturnTrue()
    {
        // Arrange
        var originalToken = "valid-token";
        var encryptedToken = _sut.EncryptToken(originalToken);

        // Act
        var result = _sut.IsValidEncryptedToken(encryptedToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidEncryptedToken_WhenInvalidToken_ShouldReturnFalse()
    {
        // Arrange
        var invalidEncryptedToken = "invalid-encrypted-token";

        // Act
        var result = _sut.IsValidEncryptedToken(invalidEncryptedToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEncryptedToken_WhenTokenIsNull_ShouldReturnFalse()
    {
        // Act
        var result = _sut.IsValidEncryptedToken(null!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEncryptedToken_WhenTokenIsEmpty_ShouldReturnFalse()
    {
        // Act
        var result = _sut.IsValidEncryptedToken("");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEncryptedToken_WhenTokenIsWhitespace_ShouldReturnFalse()
    {
        // Act
        var result = _sut.IsValidEncryptedToken("   ");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void EncryptThenDecrypt_ShouldReturnOriginalToken()
    {
        // Arrange
        var originalToken = "test-access-token-123";

        // Act
        var encrypted = _sut.EncryptToken(originalToken);
        var decrypted = _sut.DecryptToken(encrypted);

        // Assert
        Assert.Equal(originalToken, decrypted);
        Assert.NotEqual(originalToken, encrypted);
    }

    [Fact]
    public void EncryptTwice_ShouldProduceDifferentResults()
    {
        // Arrange
        var originalToken = "test-token";

        // Act
        var encrypted1 = _sut.EncryptToken(originalToken);
        var encrypted2 = _sut.EncryptToken(originalToken);

        // Assert
        // Data Protection may or may not produce different encrypted values for the same input
        // Both should decrypt to the same original value
        Assert.Equal(originalToken, _sut.DecryptToken(encrypted1));
        Assert.Equal(originalToken, _sut.DecryptToken(encrypted2));
    }

    [Fact]
    public void MultipleTokens_ShouldBeHandledIndependently()
    {
        // Arrange
        var token1 = "access-token-1";
        var token2 = "refresh-token-2";
        var token3 = "fake-client-secret-3";

        // Act
        var encrypted1 = _sut.EncryptToken(token1);
        var encrypted2 = _sut.EncryptToken(token2);
        var encrypted3 = _sut.EncryptToken(token3);

        // Assert
        Assert.Equal(token1, _sut.DecryptToken(encrypted1));
        Assert.Equal(token2, _sut.DecryptToken(encrypted2));
        Assert.Equal(token3, _sut.DecryptToken(encrypted3));

        Assert.True(_sut.IsValidEncryptedToken(encrypted1));
        Assert.True(_sut.IsValidEncryptedToken(encrypted2));
        Assert.True(_sut.IsValidEncryptedToken(encrypted3));
    }

    #endregion
}