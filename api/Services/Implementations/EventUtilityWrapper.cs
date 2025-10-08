using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Services.Implementations;

public class EventUtilityWrapper : IEventUtility
{
  public Event ConstructEvent(string payload, string signature, string secret)
  {
    return EventUtility.ConstructEvent(payload, signature, secret);
  }
}