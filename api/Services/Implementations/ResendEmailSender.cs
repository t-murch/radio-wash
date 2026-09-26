using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RadioWash.Api.Configuration;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Sends contact emails through Resend's REST API (POST /emails). A typed HttpClient rather
/// than the Resend SDK: the integration is one endpoint with a bearer header, not worth a
/// package dependency. Missing configuration fails at first use rather than at startup
/// (AppleDeveloperTokenProvider pattern) so an unconfigured deployment still boots and
/// submissions still persist.
/// </summary>
public class ResendEmailSender : IContactEmailSender
{
  private const int MaxErrorBodyLength = 500;

  private readonly HttpClient _httpClient;
  private readonly ResendSettings _resendSettings;
  private readonly ContactSettings _contactSettings;

  public ResendEmailSender(
      HttpClient httpClient,
      IOptions<ResendSettings> resendSettings,
      IOptions<ContactSettings> contactSettings)
  {
    _httpClient = httpClient;
    _resendSettings = resendSettings.Value;
    _contactSettings = contactSettings.Value;
  }

  public async Task SendAsync(ContactSubmission submission, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(_resendSettings.ApiKey))
    {
      throw new InvalidOperationException("Resend:ApiKey is not configured; cannot send contact emails.");
    }
    if (string.IsNullOrWhiteSpace(_contactSettings.RecipientEmail))
    {
      throw new InvalidOperationException("Contact:RecipientEmail is not configured; cannot send contact emails.");
    }

    // Plain text only: the message is untrusted visitor input, and a text body leaves no HTML
    // injection surface. reply_to lets the owner answer the submitter directly from the inbox.
    var payload = new
    {
      from = _contactSettings.FromEmail,
      to = new[] { _contactSettings.RecipientEmail },
      reply_to = new[] { submission.Email },
      subject = $"[RadioWash contact] {submission.Name}",
      text = BuildBody(submission)
    };

    using var request = new HttpRequestMessage(HttpMethod.Post, $"{_resendSettings.BaseUrl.TrimEnd('/')}/emails");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendSettings.ApiKey);
    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    var response = await _httpClient.SendAsync(request, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      var body = await response.Content.ReadAsStringAsync(cancellationToken);
      var truncated = body.Length <= MaxErrorBodyLength ? body : body[..MaxErrorBodyLength];
      throw new HttpRequestException(
          $"Resend returned {(int)response.StatusCode} for contact submission {submission.Id}: {truncated}");
    }
  }

  private static string BuildBody(ContactSubmission submission)
  {
    var builder = new StringBuilder();
    builder.AppendLine("New contact form submission.");
    builder.AppendLine();
    builder.AppendLine($"Name: {submission.Name}");
    builder.AppendLine($"Email: {submission.Email}");
    builder.AppendLine($"User: {submission.SupabaseUserId ?? "anonymous"}");
    builder.AppendLine($"Submitted: {submission.CreatedAt:u}");
    builder.AppendLine();
    builder.AppendLine("Message:");
    builder.AppendLine(submission.Message);
    return builder.ToString();
  }
}
