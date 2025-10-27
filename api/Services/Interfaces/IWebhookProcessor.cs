namespace RadioWash.Api.Services.Interfaces;

/// <summary>
/// Processes webhook events without retry logic to avoid circular dependencies
/// </summary>
public interface IWebhookProcessor
{
  /// <summary>
  /// Processes a webhook event payload
  /// </summary>
  Task ProcessWebhookAsync(string payload, string signature);
}