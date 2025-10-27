using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Services.Implementations;

public class ErrorClassifier : IErrorClassifier
{
    public bool IsRetryableError(Exception exception)
    {
        // Network errors are retryable
        if (exception is HttpRequestException or TaskCanceledException)
        {
            return true;
        }

        // Stripe specific retryable errors
        if (exception is StripeException stripeEx)
        {
            return stripeEx.StripeError?.Type switch
            {
                "api_connection_error" => true,
                "api_error" => true,
                "rate_limit_error" => true,
                "authentication_error" => false, // Don't retry auth errors
                "invalid_request_error" => false, // Don't retry invalid requests
                "card_error" => false, // Don't retry card errors
                _ => false
            };
        }

        // Database timeout errors are retryable
        if (exception is TimeoutException)
        {
            return true;
        }

        // Generic database errors might be retryable
        if (exception is DbUpdateException)
        {
            return true;
        }

        // By default, don't retry unknown errors
        return false;
    }
}