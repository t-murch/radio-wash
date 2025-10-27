using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.TestHelpers;

public class TestDateTimeProvider : IDateTimeProvider
{
    private DateTime _utcNow = DateTime.UtcNow;

    public DateTime UtcNow => _utcNow;

    public void SetUtcNow(DateTime utcNow)
    {
        _utcNow = utcNow;
    }

    public void AdvanceTime(TimeSpan amount)
    {
        _utcNow = _utcNow.Add(amount);
    }
}