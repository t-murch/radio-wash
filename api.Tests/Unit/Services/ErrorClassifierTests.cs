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
    public void IsRetryableError_WithBareDbUpdateException_ShouldReturnFalse()
    {
        // A DbUpdateException with no inner exception carries no signal about WHY the update
        // failed. Defaulting to "retryable" risks re-issuing a failing command that will fail
        // the same way (e.g., logic bug, schema drift). Default-deny; retry only when the
        // inner exception tells us the failure is transient.
        var exception = new DbUpdateException("Database update failed");

        var result = _errorClassifier.IsRetryableError(exception);

        Assert.False(result);
    }

    [Fact]
    public void IsRetryableError_WithDbUpdateExceptionWrappingTimeoutException_ShouldReturnTrue()
    {
        var exception = new DbUpdateException(
            "Statement timeout",
            new TimeoutException("Command timed out"));

        var result = _errorClassifier.IsRetryableError(exception);

        Assert.True(result);
    }

    [Theory]
    [InlineData("40001", "serialization_failure")]
    [InlineData("40P01", "deadlock_detected")]
    [InlineData("55P03", "lock_not_available")]
    [InlineData("57014", "query_canceled (statement_timeout)")]
    [InlineData("08000", "connection_exception")]
    [InlineData("08001", "sqlclient_unable_to_establish_sqlconnection")]
    [InlineData("08006", "connection_failure")]
    [InlineData("08003", "connection_does_not_exist")]
    public void IsRetryableError_WithRetryablePostgresSqlState_ShouldReturnTrue(string sqlState, string reason)
    {
        var pgEx = MakePostgresException(sqlState);
        var exception = new DbUpdateException($"Postgres {reason}", pgEx);

        var result = _errorClassifier.IsRetryableError(exception);

        Assert.True(result, $"SQL state {sqlState} ({reason}) should be retryable");
    }

    [Fact]
    public void IsRetryableError_WithUniqueViolation_ShouldReturnFalse()
    {
        // 23505 (unique_violation) is a correctness failure — retrying will hit the same
        // constraint. Must never be retried.
        var pgEx = MakePostgresException("23505");
        var exception = new DbUpdateException("duplicate key value", pgEx);

        var result = _errorClassifier.IsRetryableError(exception);

        Assert.False(result);
    }

    [Theory]
    [InlineData("23503")] // foreign_key_violation
    [InlineData("23502")] // not_null_violation
    [InlineData("23514")] // check_violation
    [InlineData("42P01")] // undefined_table
    [InlineData("42703")] // undefined_column
    public void IsRetryableError_WithUnknownOrPermanentPostgresSqlState_ShouldReturnFalse(string sqlState)
    {
        // SQL states not on the retryable allowlist — including other integrity violations and
        // schema errors — must default to non-retryable so we don't waste retries on logic bugs.
        var pgEx = MakePostgresException(sqlState);
        var exception = new DbUpdateException($"Postgres error {sqlState}", pgEx);

        var result = _errorClassifier.IsRetryableError(exception);

        Assert.False(result);
    }

    private static Npgsql.PostgresException MakePostgresException(string sqlState) =>
        new("test error", "ERROR", "ERROR", sqlState);

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
    // A bare DbUpdateException with no inner exception is no longer classified as retryable —
    // see IsRetryableError_WithBareDbUpdateException_ShouldReturnFalse above.
    [InlineData(typeof(DbUpdateException), false)]
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