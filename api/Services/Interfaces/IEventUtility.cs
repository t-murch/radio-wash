using Stripe;

namespace RadioWash.Api.Services.Interfaces;

public interface IEventUtility
{
  Event ConstructEvent(string payload, string signature, string secret);
}