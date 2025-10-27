namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Abstraction for DateTime operations to enable deterministic testing
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>
    /// Gets the current UTC time
    /// </summary>
    DateTime UtcNow { get; }
}