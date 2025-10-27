using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.TestHelpers;

public class TestRandomProvider : IRandomProvider
{
    private double _fixedValue = 0.5;

    public double NextDouble() => _fixedValue;

    public void SetFixedValue(double value)
    {
        if (value < 0.0 || value >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be between 0.0 (inclusive) and 1.0 (exclusive)");
        
        _fixedValue = value;
    }
}