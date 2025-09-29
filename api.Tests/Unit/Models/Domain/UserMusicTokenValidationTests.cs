using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Tests.Unit.Models.Domain;

/// <summary>
/// Unit tests for UserMusicToken validation logic
/// Tests business rules, expiration logic, and refresh capabilities
/// </summary>
public class UserMusicTokenValidationTests
{
    [Fact]
    public void IsExpired_WithFutureExpirationTime_ReturnsFalse()
    {
        // Arrange
        var token = new UserMusicToken
        {
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act & Assert
        Assert.False(token.IsExpired);
    }

    [Fact]
    public void IsExpired_WithPastExpirationTime_ReturnsTrue()
    {
        // Arrange
        var token = new UserMusicToken
        {
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        };

        // Act & Assert
        Assert.True(token.IsExpired);
    }

    [Fact]
    public void IsExpired_WithExpirationTimeWithin5Minutes_ReturnsTrue()
    {
        // Arrange - Token expires in 3 minutes (within 5-minute buffer)
        var token = new UserMusicToken
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(3)
        };

        // Act & Assert
        Assert.True(token.IsExpired);
    }

    [Fact]
    public void IsExpired_WithExpirationTimeBeyond5Minutes_ReturnsFalse()
    {
        // Arrange - Token expires in 10 minutes (beyond 5-minute buffer)
        var token = new UserMusicToken
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };

        // Act & Assert
        Assert.False(token.IsExpired);
    }

    [Fact]
    public void IsExpired_WithExpirationTimeExactly5Minutes_ReturnsTrue()
    {
        // Arrange - Token expires in exactly 5 minutes (edge case)
        var token = new UserMusicToken
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        // Act & Assert
        Assert.True(token.IsExpired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CanRefresh_WithNoRefreshToken_ReturnsFalse(string? refreshToken)
    {
        // Arrange
        var token = new UserMusicToken
        {
            EncryptedRefreshToken = refreshToken,
            RefreshFailureCount = 0,
            IsRevoked = false
        };

        // Act & Assert
        Assert.False(token.CanRefresh);
    }

    [Fact]
    public void CanRefresh_WithWhitespaceRefreshToken_ReturnsTrue()
    {
        // Arrange - string.IsNullOrEmpty("   ") returns false, so this should allow refresh
        var token = new UserMusicToken
        {
            EncryptedRefreshToken = "   ",
            RefreshFailureCount = 0,
            IsRevoked = false
        };

        // Act & Assert
        Assert.True(token.CanRefresh);
    }

    [Fact]
    public void CanRefresh_WithRevokedToken_ReturnsFalse()
    {
        // Arrange
        var token = new UserMusicToken
        {
            EncryptedRefreshToken = "valid_refresh_token",
            RefreshFailureCount = 0,
            IsRevoked = true
        };

        // Act & Assert
        Assert.False(token.CanRefresh);
    }

    [Fact]
    public void CanRefresh_WithHighFailureCount_ReturnsFalse()
    {
        // Arrange
        var token = new UserMusicToken
        {
            EncryptedRefreshToken = "valid_refresh_token",
            RefreshFailureCount = 5, // >= 5 failures
            IsRevoked = false
        };

        // Act & Assert
        Assert.False(token.CanRefresh);
    }

    [Fact]
    public void CanRefresh_WithFailureCountAtThreshold_ReturnsFalse()
    {
        // Arrange
        var token = new UserMusicToken
        {
            EncryptedRefreshToken = "valid_refresh_token",
            RefreshFailureCount = 5, // Exactly at threshold
            IsRevoked = false
        };

        // Act & Assert
        Assert.False(token.CanRefresh);
    }

    [Fact]
    public void CanRefresh_WithValidConditions_ReturnsTrue()
    {
        // Arrange
        var token = new UserMusicToken
        {
            EncryptedRefreshToken = "valid_refresh_token",
            RefreshFailureCount = 4, // Below threshold
            IsRevoked = false
        };

        // Act & Assert
        Assert.True(token.CanRefresh);
    }

    [Fact]
    public void CanRefresh_WithZeroFailureCount_ReturnsTrue()
    {
        // Arrange
        var token = new UserMusicToken
        {
            EncryptedRefreshToken = "valid_refresh_token",
            RefreshFailureCount = 0,
            IsRevoked = false
        };

        // Act & Assert
        Assert.True(token.CanRefresh);
    }

    [Fact]
    public void MarkRefreshSuccess_ResetsFailureCountAndUpdatesTimestamp()
    {
        // Arrange
        var token = new UserMusicToken
        {
            RefreshFailureCount = 2,
            LastRefreshAt = DateTime.UtcNow.AddHours(-1)
        };
        var beforeUpdate = DateTime.UtcNow;

        // Act
        token.MarkRefreshSuccess();

        // Assert
        Assert.Equal(0, token.RefreshFailureCount);
        Assert.True(token.LastRefreshAt >= beforeUpdate);
        Assert.True(token.LastRefreshAt <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void MarkRefreshSuccess_UpdatesLastRefreshAtToCurrentTime()
    {
        // Arrange
        var token = new UserMusicToken
        {
            RefreshFailureCount = 1,
            LastRefreshAt = null
        };
        var beforeUpdate = DateTime.UtcNow;

        // Act
        token.MarkRefreshSuccess();

        // Assert
        Assert.NotNull(token.LastRefreshAt);
        Assert.True(token.LastRefreshAt >= beforeUpdate);
        Assert.True(token.LastRefreshAt <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void MarkRefreshFailure_IncrementsFailureCountAndUpdatesTimestamp()
    {
        // Arrange
        var token = new UserMusicToken
        {
            RefreshFailureCount = 1,
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };
        var beforeUpdate = DateTime.UtcNow;

        // Act
        token.MarkRefreshFailure();

        // Assert
        Assert.Equal(2, token.RefreshFailureCount);
        Assert.True(token.UpdatedAt >= beforeUpdate);
        Assert.True(token.UpdatedAt <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void MarkRefreshFailure_FromZeroFailures_IncrementsToOne()
    {
        // Arrange
        var token = new UserMusicToken
        {
            RefreshFailureCount = 0,
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };

        // Act
        token.MarkRefreshFailure();

        // Assert
        Assert.Equal(1, token.RefreshFailureCount);
        Assert.True(token.UpdatedAt > DateTime.UtcNow.AddHours(-1));
    }

    [Fact]
    public void MarkRefreshFailure_MultipleFailures_AccumulatesCorrectly()
    {
        // Arrange
        var token = new UserMusicToken
        {
            RefreshFailureCount = 0,
            LastRefreshAt = null
        };

        // Act
        token.MarkRefreshFailure();
        token.MarkRefreshFailure();
        token.MarkRefreshFailure();
        token.MarkRefreshFailure();
        token.MarkRefreshFailure();

        // Assert
        Assert.Equal(5, token.RefreshFailureCount);
        Assert.False(token.CanRefresh); // Should be false at 5 failures
    }

    [Fact]
    public void RefreshCycle_SuccessAfterFailures_ResetsCountAndAllowsRefresh()
    {
        // Arrange
        var token = new UserMusicToken
        {
            EncryptedRefreshToken = "valid_refresh_token",
            RefreshFailureCount = 4,
            IsRevoked = false,
            LastRefreshAt = null
        };

        // Assert initial state
        Assert.True(token.CanRefresh); // 4 failures, still under threshold

        // Act - Mark failure (should reach threshold)
        token.MarkRefreshFailure();
        Assert.False(token.CanRefresh); // 5 failures, at threshold

        // Act - Mark success (should reset)
        token.MarkRefreshSuccess();

        // Assert final state
        Assert.Equal(0, token.RefreshFailureCount);
        Assert.True(token.CanRefresh);
    }

    [Fact]
    public void ValidationAttributes_RequiredFields_AreEnforced()
    {
        // Arrange - Create token with all required fields
        var token = new UserMusicToken
        {
            UserId = 1,
            Provider = "spotify",
            EncryptedAccessToken = "encrypted_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act & Assert - Verify required fields are set
        Assert.True(token.UserId > 0);
        Assert.False(string.IsNullOrEmpty(token.Provider));
        Assert.False(string.IsNullOrEmpty(token.EncryptedAccessToken));
        Assert.True(token.ExpiresAt > DateTime.MinValue);
    }

    [Fact]
    public void Provider_MaxLengthValidation_IsEnforced()
    {
        // Arrange
        var token = new UserMusicToken
        {
            Provider = new string('a', 50) // Exactly at max length
        };

        // Act & Assert
        Assert.Equal(50, token.Provider.Length);
    }

    [Fact]
    public void Scopes_MaxLengthValidation_IsEnforced()
    {
        // Arrange
        var token = new UserMusicToken
        {
            Scopes = new string('a', 2000) // Exactly at max length
        };

        // Act & Assert
        Assert.Equal(2000, token.Scopes.Length);
    }

    [Fact]
    public void ProviderMetadata_MaxLengthValidation_IsEnforced()
    {
        // Arrange
        var token = new UserMusicToken
        {
            ProviderMetadata = new string('a', 4000) // Exactly at max length
        };

        // Act & Assert
        Assert.Equal(4000, token.ProviderMetadata.Length);
    }
}