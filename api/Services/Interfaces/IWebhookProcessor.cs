namespace RadioWash.Api.Services.Interfaces;

public interface IWebhookProcessor
{
    /// <summary>
    /// Processes a webhook payload without retry logic
    /// </summary>
    /// <param name="payload">The webhook payload</param>
    /// <param name="signature">The webhook signature</param>
    Task ProcessWebhookAsync(string payload, string signature);
}