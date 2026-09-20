using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RadioWash.Api.Configuration;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.Implementations;

namespace RadioWash.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for ResendEmailSender
/// Tests the outbound Resend request shape (endpoint, auth, addressing) and the failure
/// behavior that the retrying email job depends on
/// </summary>
public class ResendEmailSenderTests
{
  private readonly Mock<HttpMessageHandler> _mockHandler;
  private readonly ResendSettings _resendSettings;
  private readonly ContactSettings _contactSettings;
  private HttpRequestMessage? _capturedRequest;
  private string? _capturedRequestBody;

  public ResendEmailSenderTests()
  {
    _mockHandler = new Mock<HttpMessageHandler>();
    _resendSettings = new ResendSettings { ApiKey = "re_test_key" };
    _contactSettings = new ContactSettings { RecipientEmail = "owner@example.com" };
  }

  [Fact]
  public async Task SendAsync_PostsToEmailsEndpointWithBearerAuth()
  {
    // Arrange
    var sender = CreateSender(HttpStatusCode.OK);

    // Act
    await sender.SendAsync(CreateTestSubmission());

    // Assert
    Assert.NotNull(_capturedRequest);
    Assert.Equal(HttpMethod.Post, _capturedRequest!.Method);
    Assert.Equal("https://api.resend.com/emails", _capturedRequest.RequestUri!.ToString());
    Assert.Equal("Bearer", _capturedRequest.Headers.Authorization?.Scheme);
    Assert.Equal("re_test_key", _capturedRequest.Headers.Authorization?.Parameter);
  }

  [Fact]
  public async Task SendAsync_SetsFromRecipientAndReplyToFromSettingsAndSubmission()
  {
    // Arrange
    var sender = CreateSender(HttpStatusCode.OK);
    var submission = CreateTestSubmission();

    // Act
    await sender.SendAsync(submission);

    // Assert
    Assert.NotNull(_capturedRequestBody);
    using var body = JsonDocument.Parse(_capturedRequestBody!);
    var root = body.RootElement;

    Assert.Equal(_contactSettings.FromEmail, root.GetProperty("from").GetString());
    Assert.Equal("owner@example.com", root.GetProperty("to")[0].GetString());
    Assert.Equal(submission.Email, root.GetProperty("reply_to")[0].GetString());
    Assert.Contains(submission.Name, root.GetProperty("subject").GetString());
    Assert.Contains(submission.Message, root.GetProperty("text").GetString());
  }

  [Fact]
  public async Task SendAsync_WhenResendReturnsError_ThrowsWithStatusCode()
  {
    // Arrange
    var sender = CreateSender(HttpStatusCode.UnprocessableEntity, "{\"message\":\"invalid from\"}");

    // Act & Assert
    var exception = await Assert.ThrowsAsync<HttpRequestException>(
        () => sender.SendAsync(CreateTestSubmission()));
    Assert.Contains("422", exception.Message);
  }

  [Fact]
  public async Task SendAsync_WithMissingApiKey_ThrowsInvalidOperationException()
  {
    // Arrange
    _resendSettings.ApiKey = null;
    var sender = CreateSender(HttpStatusCode.OK);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => sender.SendAsync(CreateTestSubmission()));
    Assert.Null(_capturedRequest);
  }

  private ResendEmailSender CreateSender(HttpStatusCode responseStatus, string responseBody = "{\"id\":\"email_123\"}")
  {
    _mockHandler
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
        {
          _capturedRequest = request;
          // Content must be read before the request is disposed by HttpClient.
          _capturedRequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
        })
        .ReturnsAsync(new HttpResponseMessage(responseStatus)
        {
          Content = new StringContent(responseBody)
        });

    return new ResendEmailSender(
        new HttpClient(_mockHandler.Object),
        Options.Create(_resendSettings),
        Options.Create(_contactSettings));
  }

  private static ContactSubmission CreateTestSubmission()
  {
    return new ContactSubmission
    {
      Id = 42,
      Name = "Ada Lovelace",
      Email = "ada@example.com",
      Message = "The sync feature stopped working for me yesterday.",
      EmailStatus = ContactEmailStatus.Pending,
      CreatedAt = DateTime.UtcNow.AddMinutes(-1)
    };
  }
}
