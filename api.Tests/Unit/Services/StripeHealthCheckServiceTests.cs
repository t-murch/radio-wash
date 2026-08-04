using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Services.Implementations;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Tests ValidateConfigurationAsync: key presence, key prefix shape, and the
/// production guard that refuses test-mode keys. Connectivity testing hits the
/// live Stripe API and is not unit tested.
/// </summary>
public class StripeHealthCheckServiceTests
{
  private readonly Mock<IHostEnvironment> _mockEnvironment;
  private readonly Mock<ILogger<StripeHealthCheckService>> _mockLogger;

  public StripeHealthCheckServiceTests()
  {
    _mockEnvironment = new Mock<IHostEnvironment>();
    _mockLogger = new Mock<ILogger<StripeHealthCheckService>>();
  }

  private StripeHealthCheckService CreateService(
      string environmentName,
      string? secretKey,
      string? webhookSecret,
      string? publishableKey = null)
  {
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["Stripe:SecretKey"] = secretKey,
          ["Stripe:WebhookSecret"] = webhookSecret,
          ["Stripe:PublishableKey"] = publishableKey
        })
        .Build();

    // IsProduction() is an extension method over EnvironmentName
    _mockEnvironment.SetupGet(x => x.EnvironmentName).Returns(environmentName);

    return new StripeHealthCheckService(configuration, _mockEnvironment.Object, _mockLogger.Object);
  }

  [Fact]
  public async Task ValidateConfigurationAsync_WithLiveKeysInProduction_ReturnsTrue()
  {
    var service = CreateService(
        Environments.Production,
        secretKey: "sk_live_123",
        webhookSecret: "whsec_123",
        publishableKey: "pk_live_123");

    Assert.True(await service.ValidateConfigurationAsync());
  }

  [Fact]
  public async Task ValidateConfigurationAsync_WithTestSecretKeyInProduction_ReturnsFalse()
  {
    var service = CreateService(
        Environments.Production,
        secretKey: "sk_test_123",
        webhookSecret: "whsec_123");

    Assert.False(await service.ValidateConfigurationAsync());
  }

  [Fact]
  public async Task ValidateConfigurationAsync_WithTestPublishableKeyInProduction_ReturnsFalse()
  {
    var service = CreateService(
        Environments.Production,
        secretKey: "sk_live_123",
        webhookSecret: "whsec_123",
        publishableKey: "pk_test_123");

    Assert.False(await service.ValidateConfigurationAsync());
  }

  [Fact]
  public async Task ValidateConfigurationAsync_WithTestKeysInDevelopment_ReturnsTrue()
  {
    var service = CreateService(
        Environments.Development,
        secretKey: "sk_test_123",
        webhookSecret: "whsec_123",
        publishableKey: "pk_test_123");

    Assert.True(await service.ValidateConfigurationAsync());
  }

  [Fact]
  public async Task ValidateConfigurationAsync_WithMissingSecretKey_ReturnsFalse()
  {
    var service = CreateService(
        Environments.Development,
        secretKey: null,
        webhookSecret: "whsec_123");

    Assert.False(await service.ValidateConfigurationAsync());
  }

  [Fact]
  public async Task ValidateConfigurationAsync_WithMissingWebhookSecret_ReturnsFalse()
  {
    var service = CreateService(
        Environments.Development,
        secretKey: "sk_test_123",
        webhookSecret: null);

    Assert.False(await service.ValidateConfigurationAsync());
  }

  [Fact]
  public async Task ValidateConfigurationAsync_WithMalformedSecretKeyPrefix_ReturnsFalse()
  {
    var service = CreateService(
        Environments.Development,
        secretKey: "not_a_stripe_key",
        webhookSecret: "whsec_123");

    Assert.False(await service.ValidateConfigurationAsync());
  }

  [Fact]
  public async Task ValidateConfigurationAsync_WithMalformedWebhookSecretPrefix_ReturnsFalse()
  {
    var service = CreateService(
        Environments.Development,
        secretKey: "sk_test_123",
        webhookSecret: "not_a_webhook_secret");

    Assert.False(await service.ValidateConfigurationAsync());
  }
}
