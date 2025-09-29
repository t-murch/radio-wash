namespace RadioWash.Api.Services.Interfaces;

public interface IPaymentService
{
  Task<string> CreateCheckoutSessionAsync(int userId, string planPriceId);
  Task<string> CreatePortalSessionAsync(string customerId);
  Task HandleWebhookAsync(string payload, string signature);
}
