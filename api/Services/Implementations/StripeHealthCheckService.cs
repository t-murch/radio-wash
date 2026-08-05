using RadioWash.Api.Services.Interfaces;
using Stripe;

namespace RadioWash.Api.Services.Implementations;

public class StripeHealthCheckService : IStripeHealthCheckService
{
  private readonly IConfiguration _configuration;
  private readonly IHostEnvironment _environment;
  private readonly ILogger<StripeHealthCheckService> _logger;

  public StripeHealthCheckService(
      IConfiguration configuration,
      IHostEnvironment environment,
      ILogger<StripeHealthCheckService> logger)
  {
    _configuration = configuration;
    _environment = environment;
    _logger = logger;
  }

  public Task<bool> ValidateConfigurationAsync()
  {
    try
    {
      var secretKey = _configuration["Stripe:SecretKey"];
      var publishableKey = _configuration["Stripe:PublishableKey"];
      var webhookSecret = _configuration["Stripe:WebhookSecret"];

      if (string.IsNullOrEmpty(secretKey))
      {
        _logger.LogError("Stripe:SecretKey is not configured");
        return Task.FromResult(false);
      }

      if (string.IsNullOrEmpty(webhookSecret))
      {
        _logger.LogError("Stripe:WebhookSecret is not configured");
        return Task.FromResult(false);
      }

      if (!secretKey.StartsWith("sk_"))
      {
        _logger.LogError("Stripe:SecretKey does not appear to be a valid Stripe secret key");
        return Task.FromResult(false);
      }

      if (!webhookSecret.StartsWith("whsec_"))
      {
        _logger.LogError("Stripe:WebhookSecret does not appear to be a valid Stripe webhook secret");
        return Task.FromResult(false);
      }

      // Test-mode keys in Production mean real users see checkout against a sandbox — fail
      // startup rather than run in a silently broken mode.
      if (_environment.IsProduction())
      {
        if (secretKey.StartsWith("sk_test_"))
        {
          _logger.LogError("Stripe:SecretKey is a test-mode key but the environment is Production");
          return Task.FromResult(false);
        }

        if (publishableKey?.StartsWith("pk_test_") == true)
        {
          _logger.LogError("Stripe:PublishableKey is a test-mode key but the environment is Production");
          return Task.FromResult(false);
        }
      }

      _logger.LogInformation("Stripe configuration validation passed");
      return Task.FromResult(true);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error validating Stripe configuration");
      return Task.FromResult(false);
    }
  }

  public async Task<bool> TestConnectivityAsync()
  {
    try
    {
      var secretKey = _configuration["Stripe:SecretKey"];
      if (string.IsNullOrEmpty(secretKey))
      {
        return false;
      }

      StripeConfiguration.ApiKey = secretKey;
      
      // Make a simple API call to test connectivity
      var balanceService = new BalanceService();
      var balance = await balanceService.GetAsync();

      if (balance != null)
      {
        _logger.LogInformation("Stripe connectivity test passed - balance retrieved successfully");
        return true;
      }

      _logger.LogError("Stripe connectivity test failed: Invalid balance response");
      return false;
    }
    catch (StripeException ex)
    {
      _logger.LogError(ex, "Stripe connectivity test failed: {ErrorCode} - {ErrorMessage}", 
          ex.StripeError?.Code, ex.StripeError?.Message);
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Stripe connectivity test failed due to unexpected error");
      return false;
    }
  }
}