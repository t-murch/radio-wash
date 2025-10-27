namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Abstraction for random number generation to enable deterministic testing
/// </summary>
public interface IRandomProvider
{
    /// <summary>
    /// Returns a random double between 0.0 and 1.0
    /// </summary>
    double NextDouble();
}