namespace RadioWash.Api.Services.Exceptions;

// Thrown by SubscriptionService.EnforcePlanLimitAsync when a user at their plan's limit
// attempts to create another resource. The middleware maps this to HTTP 403 with a
// Problem Details body that surfaces both limit and current count so the UI can render
// a specific message instead of a generic error.
public class PlanLimitExceededException : Exception
{
    public string LimitType { get; }
    public int Limit { get; }
    public int Current { get; }

    public PlanLimitExceededException(string limitType, int limit, int current)
        : base($"Plan limit exceeded: {current}/{limit} {limitType}")
    {
        LimitType = limitType;
        Limit = limit;
        Current = current;
    }
}
