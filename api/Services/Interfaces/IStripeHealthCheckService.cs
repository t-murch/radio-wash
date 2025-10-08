namespace RadioWash.Api.Services.Interfaces;

public interface IStripeHealthCheckService
{
  Task<bool> ValidateConfigurationAsync();
  Task<bool> TestConnectivityAsync();
}