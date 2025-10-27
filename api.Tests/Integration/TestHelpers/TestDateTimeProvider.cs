using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Integration.TestHelpers;

/// <summary>
/// Test implementation of IDateTimeProvider for deterministic testing
/// </summary>
public class TestDateTimeProvider : IDateTimeProvider
{
    private DateTime _utcNow;

    public TestDateTimeProvider(DateTime? utcNow = null)
    {
        _utcNow = utcNow ?? new DateTime(2024, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    }

    public DateTime UtcNow => _utcNow;

    /// <summary>
    /// Sets the current time for testing
    /// </summary>
    public void SetUtcNow(DateTime utcNow)
    {
        _utcNow = utcNow;
    }

    /// <summary>
    /// Advances time by the specified amount
    /// </summary>
    public void AdvanceTime(TimeSpan amount)
    {
        _utcNow = _utcNow.Add(amount);
    }
}