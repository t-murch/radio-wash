using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Services.Implementations;

public class StripeWebhookOrchestrator : IWebhookOrchestrator
{
    private readonly IConfiguration _configuration;
    private readonly IEventUtility _eventUtility;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IWebhookRetryService _webhookRetryService;
    private readonly IWebhookProcessor _webhookProcessor;
    private readonly ILogger<StripeWebhookOrchestrator> _logger;

    public StripeWebhookOrchestrator(
        IConfiguration configuration,
        IEventUtility eventUtility,
        IIdempotencyService idempotencyService,
        IWebhookRetryService webhookRetryService,
        IWebhookProcessor webhookProcessor,
        ILogger<StripeWebhookOrchestrator> logger)
    {
        _configuration = configuration;
        _eventUtility = eventUtility;
        _idempotencyService = idempotencyService;
        _webhookRetryService = webhookRetryService;
        _webhookProcessor = webhookProcessor;
        _logger = logger;
    }

    public async Task HandleWebhookAsync(string payload, string signature)
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];

        if (string.IsNullOrEmpty(webhookSecret))
        {
            _logger.LogError("Stripe webhook secret is not configured");
            throw new InvalidOperationException("Stripe webhook secret is not configured");
        }

        try
        {
            var stripeEvent = _eventUtility.ConstructEvent(payload, signature, webhookSecret);

            _logger.LogInformation("Processing Stripe webhook event: {EventType} with ID {EventId}",
                stripeEvent.Type, stripeEvent.Id);

            // Use idempotency service to ensure only one concurrent request processes this event
            var shouldProcess = await _idempotencyService.TryProcessEventAsync(stripeEvent.Id, stripeEvent.Type);

            if (!shouldProcess)
            {
                _logger.LogInformation("Webhook event {EventId} of type {EventType} has already been processed or claimed by another request",
                    stripeEvent.Id, stripeEvent.Type);
                return;
            }

            try
            {
                // Use the webhook processor for actual event processing
                await _webhookProcessor.ProcessWebhookAsync(stripeEvent);

                // Mark event as successfully processed
                await _idempotencyService.MarkEventSuccessfulAsync(stripeEvent.Id);

                _logger.LogInformation("Successfully processed webhook event {EventId} of type {EventType}",
                    stripeEvent.Id, stripeEvent.Type);
            }
            catch (Exception processingEx)
            {
                // Mark event as failed
                await _idempotencyService.MarkEventFailedAsync(stripeEvent.Id, processingEx.Message);

                _logger.LogError(processingEx, "Failed to process webhook event {EventId} of type {EventType}: {ErrorMessage}",
                    stripeEvent.Id, stripeEvent.Type, processingEx.Message);

                // Schedule retry if the error is retryable
                if (_webhookRetryService.IsRetryableError(processingEx))
                {
                    try
                    {
                        await _webhookRetryService.ScheduleRetryAsync(
                            stripeEvent.Id,
                            stripeEvent.Type,
                            stripeEvent.ToJson(),
                            processingEx.Message);

                        _logger.LogInformation("Scheduled retry for webhook event {EventId} due to retryable error", stripeEvent.Id);
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogError(retryEx, "Failed to schedule retry for webhook event {EventId}: {RetryError}",
                            stripeEvent.Id, retryEx.Message);
                    }
                }
                else
                {
                    _logger.LogWarning("Webhook event {EventId} failed with non-retryable error: {ErrorMessage}",
                        stripeEvent.Id, processingEx.Message);
                    await _idempotencyService.MarkEventPermanentlyFailedAsync(stripeEvent.Id, processingEx.Message);
                }

                // Return gracefully - don't throw, to avoid triggering Stripe retries
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe webhook signature verification failed: {Message}", ex.Message);
            throw;
        }
    }
}
