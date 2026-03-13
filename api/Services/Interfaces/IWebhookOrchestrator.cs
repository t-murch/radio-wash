namespace RadioWash.Api.Services.Interfaces;

public interface IWebhookOrchestrator
{
    Task HandleWebhookAsync(string payload, string signature);
}
