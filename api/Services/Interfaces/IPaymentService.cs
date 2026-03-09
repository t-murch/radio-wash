namespace RadioWash.Api.Services.Interfaces;

public interface IPaymentService
{
  Task<string> CreateCheckoutSessionAsync(int userId, int planId);
  Task<string> CreatePortalSessionAsync(string customerId);
  Task HandleWebhookAsync(string payload, string signature);
}
