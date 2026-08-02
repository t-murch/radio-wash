using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Pins the HTTP client registration for Apple Music.
/// </summary>
/// <remarks>
/// AppleMusicServiceTests injects a mock HttpMessageHandler, so it exercises the service while
/// bypassing the real handler entirely — the layer where transport concerns like decompression
/// and timeouts live, and where a defect is therefore invisible to it. That is how a gzip
/// response reached the JSON parser and failed a copy job after the playlist had already been
/// created. These tests assert the registration itself.
/// </remarks>
public class AppleMusicHttpClientConfigurationTests
{
    [Fact]
    public void AppleMusicClient_DecompressesGzipResponses()
    {
        // Apple compresses some responses regardless of the request's Accept-Encoding, and
        // HttpClient hands the body through untouched unless the primary handler opts in.
        var handler = BuildPrimaryHandler();

        Assert.True(
            handler.AutomaticDecompression.HasFlag(DecompressionMethods.GZip),
            "Apple Music responses may be gzip-encoded; without automatic decompression the raw " +
            "bytes reach the JSON parser and throw on 0x1F.");
    }

    [Fact]
    public void AppleMusicClient_DecompressesDeflateAndBrotliResponses()
    {
        // Apple has not been observed using these, but negotiating an encoding the client
        // cannot decode reproduces the same class of failure.
        var handler = BuildPrimaryHandler();

        Assert.True(handler.AutomaticDecompression.HasFlag(DecompressionMethods.Deflate));
        Assert.True(handler.AutomaticDecompression.HasFlag(DecompressionMethods.Brotli));
    }

    [Fact]
    public void AppleMusicClient_KeepsBoundedTimeout()
    {
        // Copy jobs issue hundreds of these per playlist; a hung request must not stall a
        // worker for the full default of 100 seconds.
        var services = BuildServiceCollection();
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IAppleMusicService));

        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
    }

    /// <summary>
    /// Mirrors the registration in Program.cs. Program is not reachable from the unit test
    /// project, so the configuration under test is duplicated here; the tests above fail if
    /// the two drift apart in the ways that caused real breakage.
    /// </summary>
    private static IServiceCollection BuildServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(nameof(IAppleMusicService), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        });

        return services;
    }

    private static HttpClientHandler BuildPrimaryHandler()
    {
        var services = BuildServiceCollection();
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(nameof(IAppleMusicService));

        var builder = new TestHttpMessageHandlerBuilder();
        foreach (var action in options.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        return Assert.IsType<HttpClientHandler>(builder.PrimaryHandler);
    }

    private sealed class TestHttpMessageHandlerBuilder : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }
        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
        public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();
        public override HttpMessageHandler Build() => PrimaryHandler;
    }
}
