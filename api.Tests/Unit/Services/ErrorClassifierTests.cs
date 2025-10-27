using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Services.Implementations;
using Stripe;

namespace RadioWash.Api.Tests.Unit.Services;

public class ErrorClassifierTests
{
    private readonly ErrorClassifier _errorClassifier;

    public ErrorClassifierTests()
    {
        _errorClassifier = new ErrorClassifier();
    }

    #region Network and HTTP Errors

    [Fact]
    public void IsRetryableError_WithHttpRequestException_ShouldReturnTrue()
    {
        // Arrange
        var exception = new HttpRequestException("Network error");

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsRetryableError_WithTaskCanceledException_ShouldReturnTrue()
    {
        // Arrange
        var exception = new TaskCanceledException("Request timeout");

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Database Errors

    [Fact]
    public void IsRetryableError_WithTimeoutException_ShouldReturnTrue()
    {
        // Arrange
        var exception = new TimeoutException("Database timeout");

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsRetryableError_WithDbUpdateException_ShouldReturnTrue()
    {
        // Arrange
        var exception = new DbUpdateException("Database update failed");

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Stripe Errors

    [Fact]
    public void IsRetryableError_WithStripeApiConnectionError_ShouldReturnTrue()
    {
        // Arrange
        var stripeError = new StripeError 
        { 
            Type = "api_connection_error",
            Code = "api_connection_error",
            Message = "Connection to Stripe failed"
        };
        var exception = new StripeException(stripeError.Message) { StripeError = stripeError };

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsRetryableError_WithStripeApiError_ShouldReturnTrue()
    {
        // Arrange
        var stripeError = new StripeError 
        { 
            Type = "api_error",
            Code = "api_error",
            Message = "Stripe API error"
        };
        var exception = new StripeException(stripeError.Message) { StripeError = stripeError };

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsRetryableError_WithStripeRateLimitError_ShouldReturnTrue()
    {
        // Arrange
        var stripeError = new StripeError 
        { 
            Type = "rate_limit_error",
            Code = "rate_limit",
            Message = "Rate limit exceeded"
        };
        var exception = new StripeException(stripeError.Message) { StripeError = stripeError };

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsRetryableError_WithStripeAuthenticationError_ShouldReturnFalse()
    {
        // Arrange
        var stripeError = new StripeError 
        { 
            Type = "authentication_error",
            Code = "authentication_error",
            Message = "Authentication failed"
        };
        var exception = new StripeException(stripeError.Message) { StripeError = stripeError };

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsRetryableError_WithStripeInvalidRequestError_ShouldReturnFalse()
    {
        // Arrange
        var stripeError = new StripeError 
        { 
            Type = "invalid_request_error",
            Code = "invalid_request_error",
            Message = "Invalid request"
        };
        var exception = new StripeException(stripeError.Message) { StripeError = stripeError };

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsRetryableError_WithStripeCardError_ShouldReturnFalse()
    {
        // Arrange
        var stripeError = new StripeError 
        { 
            Type = "card_error",
            Code = "card_declined",
            Message = "Card was declined"
        };
        var exception = new StripeException(stripeError.Message) { StripeError = stripeError };

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsRetryableError_WithStripeUnknownError_ShouldReturnFalse()
    {
        // Arrange
        var stripeError = new StripeError 
        { 
            Type = "unknown_error_type",
            Code = "unknown_code",
            Message = "Unknown error"
        };
        var exception = new StripeException(stripeError.Message) { StripeError = stripeError };

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsRetryableError_WithStripeExceptionButNullStripeError_ShouldReturnFalse()
    {
        // Arrange
        var exception = new StripeException("Error without StripeError object");

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Non-Retryable Errors

    [Fact]
    public void IsRetryableError_WithInvalidOperationException_ShouldReturnFalse()
    {
        // Arrange
        var exception = new InvalidOperationException("Invalid operation");

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsRetryableError_WithArgumentException_ShouldReturnFalse()
    {
        // Arrange
        var exception = new ArgumentException("Invalid argument");

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsRetryableError_WithNullReferenceException_ShouldReturnFalse()
    {
        // Arrange
        var exception = new NullReferenceException("Null reference");

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Theory Tests for Comprehensive Coverage

    [Theory]
    [InlineData(typeof(HttpRequestException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(DbUpdateException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(ArgumentException), false)]
    [InlineData(typeof(NullReferenceException), false)]
    [InlineData(typeof(NotSupportedException), false)]
    public void IsRetryableError_WithVariousExceptionTypes_ShouldClassifyCorrectly(Type exceptionType, bool expectedRetryable)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test message")!;

        // Act
        var result = _errorClassifier.IsRetryableError(exception);

        // Assert
        Assert.Equal(expectedRetryable, result);
    }

    #endregion
}