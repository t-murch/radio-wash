using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Integration.TestHelpers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace RadioWash.Api.Tests.Integration;

/// <summary>
/// End-to-end tests for the public contact endpoint: request → validation → Postgres row →
/// Resend call (faked with WireMock). The Testing environment has no Hangfire, so the
/// InlineContactEmailDispatcher runs the email job in-request — the full persist → send →
/// bookkeeping path executes before the response returns.
///
/// Prerequisites: `supabase start` (same as the other Local-Supabase integration tests).
/// Each test gets its own WireMock server and derived factory, so Resend behavior and rate
/// limiter state never leak between tests.
/// </summary>
public class ContactSubmissionIntegrationTests : IClassFixture<LocalSupabaseWebApplicationFactory>, IAsyncLifetime
{
  private readonly LocalSupabaseWebApplicationFactory _factory;
  private WireMockServer _resendServer = null!;
  private WebApplicationFactory<Program> _configuredFactory = null!;
  private HttpClient _client = null!;

  public ContactSubmissionIntegrationTests(LocalSupabaseWebApplicationFactory factory)
  {
    _factory = factory;
  }

  public async Task InitializeAsync()
  {
    _resendServer = WireMockServer.Start();

    _configuredFactory = _factory.WithWebHostBuilder(builder =>
    {
      builder.ConfigureAppConfiguration((_, config) =>
      {
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["Resend:ApiKey"] = "re_test_key",
          ["Resend:BaseUrl"] = _resendServer.Url,
          ["Contact:RecipientEmail"] = "owner@example.com"
        });
      });
    });
    _client = _configuredFactory.CreateClient();

    // The factory sets SkipMigrations=true and assumes the schema is already present; make
    // sure the ContactSubmissions table exists. Migrate() is idempotent.
    using var scope = _configuredFactory.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<RadioWashDbContext>().Database.MigrateAsync();
  }

  public Task DisposeAsync()
  {
    _client.Dispose();
    _configuredFactory.Dispose();
    _resendServer.Stop();
    _resendServer.Dispose();
    return Task.CompletedTask;
  }

  [Fact]
  public async Task PostContact_Anonymous_Returns200AndPersistsRow()
  {
    // Arrange
    StubResendSuccess();
    var email = UniqueEmail();

    // Act
    var response = await PostContactAsync(new
    {
      name = "Ada Lovelace",
      email,
      message = "The sync feature stopped working for me yesterday."
    });

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("received", body);

    var row = await FindSubmissionByEmailAsync(email);
    Assert.NotNull(row);
    Assert.Equal("Ada Lovelace", row!.Name);
    Assert.Equal("The sync feature stopped working for me yesterday.", row.Message);
    Assert.Null(row.SupabaseUserId);
    Assert.Equal(ContactEmailStatus.Sent, row.EmailStatus);
    Assert.NotNull(row.EmailSentAt);
  }

  [Fact]
  public async Task PostContact_WithValidSubmission_SendsEmailViaResendWithReplyTo()
  {
    // Arrange
    StubResendSuccess();
    var email = UniqueEmail();

    // Act
    await PostContactAsync(new { name = "Ada Lovelace", email, message = "Please help." });

    // Assert: exactly one Resend call, addressed per configuration with Reply-To pointing
    // back at the submitter.
    var resendCalls = _resendServer.FindLogEntries(Request.Create().WithPath("/emails").UsingPost()).ToList();
    var call = Assert.Single(resendCalls);

    using var sent = JsonDocument.Parse(call.RequestMessage.Body!);
    var root = sent.RootElement;
    Assert.Equal("owner@example.com", root.GetProperty("to")[0].GetString());
    Assert.Equal(email, root.GetProperty("reply_to")[0].GetString());
    Assert.StartsWith("RadioWash Contact", root.GetProperty("from").GetString());
  }

  [Fact]
  public async Task PostContact_WhenResendReturns500_StillReturns200AndRowMarkedFailed()
  {
    // Arrange: the core durability requirement — a delivery failure must not lose the
    // message or surface as a user-facing error.
    _resendServer
        .Given(Request.Create().WithPath("/emails").UsingPost())
        .RespondWith(Response.Create().WithStatusCode(500).WithBody("{\"message\":\"internal error\"}"));
    var email = UniqueEmail();

    // Act
    var response = await PostContactAsync(new { name = "Ada Lovelace", email, message = "Please help." });

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

    var row = await FindSubmissionByEmailAsync(email);
    Assert.NotNull(row);
    Assert.Equal(ContactEmailStatus.Failed, row!.EmailStatus);
    Assert.NotNull(row.EmailError);
    Assert.Null(row.EmailSentAt);
  }

  [Fact]
  public async Task PostContact_WithHoneypot_Returns200PersistsNothingAndCallsResendNever()
  {
    // Arrange
    StubResendSuccess();
    var email = UniqueEmail();

    // Act
    var response = await PostContactAsync(new
    {
      name = "Bot",
      email,
      message = "Buy now!",
      website = "https://spam.example"
    });

    // Assert: identical success response, but nothing stored and nothing sent.
    Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("received", body);

    Assert.Null(await FindSubmissionByEmailAsync(email));
    Assert.Empty(_resendServer.FindLogEntries(Request.Create().WithPath("/emails").UsingPost()));
  }

  [Fact]
  public async Task PostContact_WithInvalidBody_Returns400ProblemDetails()
  {
    // Arrange
    StubResendSuccess();

    // Act: missing email.
    var response = await PostContactAsync(new { name = "Ada Lovelace", message = "Please help." });

    // Assert
    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal("Invalid contact submission", problem.RootElement.GetProperty("title").GetString());
    Assert.Equal(
        "https://radiowash.com/problems/contact-validation",
        problem.RootElement.GetProperty("type").GetString());
  }

  private void StubResendSuccess()
  {
    _resendServer
        .Given(Request.Create().WithPath("/emails").UsingPost())
        .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"id\":\"email_123\"}"));
  }

  private async Task<HttpResponseMessage> PostContactAsync(object payload)
  {
    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    return await _client.PostAsync("/api/contact", content);
  }

  private async Task<ContactSubmission?> FindSubmissionByEmailAsync(string email)
  {
    using var scope = _configuredFactory.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
    return await dbContext.ContactSubmissions
        .AsNoTracking()
        .FirstOrDefaultAsync(cs => cs.Email == email);
  }

  private static string UniqueEmail() => $"contact-{Guid.NewGuid():N}@example.com";
}
