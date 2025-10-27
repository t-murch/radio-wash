using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Integration.TestHelpers;

/// <summary>
/// Test implementation of IRandomProvider for deterministic testing
/// </summary>
public class TestRandomProvider : IRandomProvider
{
    private readonly Queue<double> _values = new();
    private double _fixedValue = 0.5; // Default middle value

    /// <summary>
    /// Sets a fixed value to return for all NextDouble() calls
    /// </summary>
    public void SetFixedValue(double value)
    {
        if (value < 0.0 || value >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be between 0.0 (inclusive) and 1.0 (exclusive)");
        
        _fixedValue = value;
        _values.Clear();
    }

    /// <summary>
    /// Queues specific values to return in sequence
    /// </summary>
    public void QueueValues(params double[] values)
    {
        _values.Clear();
        foreach (var value in values)
        {
            if (value < 0.0 || value >= 1.0)
                throw new ArgumentOutOfRangeException(nameof(values), "All values must be between 0.0 (inclusive) and 1.0 (exclusive)");
            
            _values.Enqueue(value);
        }
    }

    public double NextDouble()
    {
        return _values.Count > 0 ? _values.Dequeue() : _fixedValue;
    }
}