namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Classifies exceptions to determine if they are retryable
/// </summary>
public interface IErrorClassifier
{
    /// <summary>
    /// Determines if an exception represents a retryable error condition
    /// </summary>
    /// <param name="exception">The exception to classify</param>
    /// <returns>True if the error is retryable, false otherwise</returns>
    bool IsRetryableError(Exception exception);
}