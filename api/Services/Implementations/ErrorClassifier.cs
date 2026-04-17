using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Services.Implementations;

public class ErrorClassifier : IErrorClassifier
{
    // Postgres SQL states we classify as transient. Retrying is safe because the underlying
    // cause (transient lock, concurrency conflict, dropped connection) clears itself. See
    // https://www.postgresql.org/docs/current/errcodes-appendix.html. Anything not on this
    // allowlist — especially integrity violations like 23505 unique_violation — is a
    // correctness problem that will fail the same way on retry.
    private static readonly HashSet<string> RetryablePostgresSqlStates = new(StringComparer.Ordinal)
    {
        "40001", // serialization_failure
        "40P01", // deadlock_detected
        "55P03", // lock_not_available
        "57014", // query_canceled (statement_timeout)
        "08000", // connection_exception
        "08001", // sqlclient_unable_to_establish_sqlconnection
        "08006", // connection_failure
        "08003", // connection_does_not_exist
    };

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

        // Database errors: only retry known transient cases. A bare DbUpdateException with no
        // inner exception carries no signal, so default-deny.
        if (exception is DbUpdateException dbUpdateEx)
        {
            if (dbUpdateEx.InnerException is TimeoutException)
            {
                return true;
            }

            if (dbUpdateEx.InnerException is Npgsql.PostgresException pgEx)
            {
                return RetryablePostgresSqlStates.Contains(pgEx.SqlState);
            }

            return false;
        }

        // By default, don't retry unknown errors
        return false;
    }
}