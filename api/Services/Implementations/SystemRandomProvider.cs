using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Production implementation of IRandomProvider using system Random
/// </summary>
public class SystemRandomProvider : IRandomProvider
{
    private readonly Random _random = new();

    public double NextDouble() => _random.NextDouble();
}