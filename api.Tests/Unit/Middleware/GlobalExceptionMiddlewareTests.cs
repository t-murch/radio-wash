using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Middleware;
using System.Text.Json;

namespace RadioWash.Api.Tests.Unit.Middleware;

/// <summary>
/// Unit tests for GlobalExceptionMiddleware
/// Tests exception handling, logging, and error response formatting
/// </summary>
public class GlobalExceptionMiddlewareTests
{
  private readonly Mock<ILogger<GlobalExceptionMiddleware>> _mockLogger;
  private readonly Mock<RequestDelegate> _mockNext;
  private readonly Mock<IWebHostEnvironment> _mockEnvironment;
  private readonly GlobalExceptionMiddleware _middleware;

  public GlobalExceptionMiddlewareTests()
  {
    _mockLogger = new Mock<ILogger<GlobalExceptionMiddleware>>();
    _mockNext = new Mock<RequestDelegate>();
    _mockEnvironment = new Mock<IWebHostEnvironment>();
    // Default most tests to Development so the legacy "details = exception.Message" contract
    // holds. Tests that care about the Production redaction path override EnvironmentName.
    _mockEnvironment.SetupGet(e => e.EnvironmentName).Returns("Development");
    _middleware = new GlobalExceptionMiddleware(_mockNext.Object, _mockLogger.Object, _mockEnvironment.Object);
  }

  [Fact]
  public async Task InvokeAsync_WithNoException_CallsNextDelegate()
  {
    // Arrange
    var context = CreateHttpContext();
    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    _mockNext.Verify(x => x(context), Times.Once);
    Assert.Equal(200, context.Response.StatusCode);
  }

  [Fact]
  public async Task InvokeAsync_WithGenericException_Returns500WithErrorMessage()
  {
    // Arrange
    var context = CreateHttpContext();
    var exception = new InvalidOperationException("Test exception message");

    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).ThrowsAsync(exception);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    Assert.Equal(500, context.Response.StatusCode);
    Assert.Equal("application/json", context.Response.ContentType);

    var responseBody = GetResponseBody(context);
    var errorResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

    Assert.NotNull(errorResponse);
    Assert.Equal("An internal server error occurred", errorResponse["error"].ToString());
    Assert.Equal("Test exception message", errorResponse["details"].ToString());
  }

  [Fact]
  public async Task InvokeAsync_WithArgumentException_Returns500WithErrorMessage()
  {
    // Arrange
    var context = CreateHttpContext();
    var exception = new ArgumentException("Invalid argument provided");

    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).ThrowsAsync(exception);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    Assert.Equal(500, context.Response.StatusCode);

    var responseBody = GetResponseBody(context);
    var errorResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

    Assert.NotNull(errorResponse);
    Assert.Equal("An internal server error occurred", errorResponse["error"].ToString());
  }

  [Fact]
  public async Task InvokeAsync_WithUnauthorizedAccessException_Returns500WithErrorMessage()
  {
    // Arrange
    var context = CreateHttpContext();
    var exception = new UnauthorizedAccessException("Access denied");

    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).ThrowsAsync(exception);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    Assert.Equal(500, context.Response.StatusCode);

    var responseBody = GetResponseBody(context);
    var errorResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

    Assert.NotNull(errorResponse);
    Assert.Equal("An internal server error occurred", errorResponse["error"].ToString());
  }

  [Fact]
  public async Task InvokeAsync_WithKeyNotFoundException_Returns500WithErrorMessage()
  {
    // Arrange
    var context = CreateHttpContext();
    var exception = new KeyNotFoundException("Resource not found");

    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).ThrowsAsync(exception);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    Assert.Equal(500, context.Response.StatusCode);

    var responseBody = GetResponseBody(context);
    var errorResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

    Assert.NotNull(errorResponse);
    Assert.Equal("An internal server error occurred", errorResponse["error"].ToString());
  }

  [Fact]
  public async Task InvokeAsync_LogsErrorWithExceptionDetails()
  {
    // Arrange
    var context = CreateHttpContext();
    var exception = new InvalidOperationException("Test exception for logging");

    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).ThrowsAsync(exception);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("An unhandled exception occurred")),
            exception,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
  }

  [Fact]
  public async Task InvokeAsync_InProduction_LogsErrorWithExceptionDetails()
  {
    // The Production environment must NOT be a log blackout — Sentry is not the only consumer
    // of structured logs. Prior to this fix, GlobalExceptionMiddleware guarded LogError with
    // `if (!_environment.IsProduction())` and swallowed the error path in prod entirely.
    var context = CreateHttpContext();
    var exception = new InvalidOperationException("Production failure");

    // Force IsProduction() == true by reporting EnvironmentName = "Production".
    _mockEnvironment.SetupGet(e => e.EnvironmentName).Returns("Production");

    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).ThrowsAsync(exception);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert — LogError must fire with the exception regardless of environment.
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("An unhandled exception occurred")),
            exception,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);

    // And the response still returns 500 with the generic error body.
    Assert.Equal(500, context.Response.StatusCode);
  }

  [Fact]
  public async Task InvokeAsync_WithSuccessfulResponse_DoesNotLogWarning()
  {
    // Arrange
    var context = CreateHttpContext();
    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Never);
  }

  [Fact]
  public async Task InvokeAsync_With400Response_LogsWarning()
  {
    // Arrange
    var context = CreateHttpContext();
    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Callback<HttpContext>(ctx =>
    {
      ctx.Response.StatusCode = 400;
    });

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("returned 400")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
  }

  [Fact]
  public async Task InvokeAsync_With404Response_LogsWarning()
  {
    // Arrange
    var context = CreateHttpContext();
    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Callback<HttpContext>(ctx =>
    {
      ctx.Response.StatusCode = 404;
    });

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("returned 404")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
  }


  [Fact]
  public async Task InvokeAsync_JsonErrorResponse_HasCorrectStructure()
  {
    // Arrange
    var context = CreateHttpContext();
    var exception = new InvalidOperationException("Test exception");

    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).ThrowsAsync(exception);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert — Development payload carries error + details + traceId.
    var responseBody = GetResponseBody(context);
    var errorResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

    Assert.NotNull(errorResponse);
    Assert.True(errorResponse.ContainsKey("error"));
    Assert.True(errorResponse.ContainsKey("details"));
    Assert.True(errorResponse.ContainsKey("traceId"));
    Assert.Equal(3, errorResponse.Count);
  }

  [Fact]
  public async Task InvokeAsync_InProduction_RedactsExceptionMessageFromResponseBody()
  {
    // Protects against leaking EF/Stripe/internal detail to API consumers. In Production the
    // response body must contain only a generic error and a trace ID for log correlation.
    var context = CreateHttpContext();
    var exception = new InvalidOperationException(
        "Npgsql.PostgresException (0x80004005): 23505: duplicate key value violates unique constraint \"user_email_key\"");

    _mockEnvironment.SetupGet(e => e.EnvironmentName).Returns("Production");
    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).ThrowsAsync(exception);

    // Act
    await _middleware.InvokeAsync(context);

    // Assert
    Assert.Equal(500, context.Response.StatusCode);
    var responseBody = GetResponseBody(context);
    var errorResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

    Assert.NotNull(errorResponse);
    Assert.Equal("An internal server error occurred", errorResponse["error"].ToString());
    Assert.True(errorResponse.ContainsKey("traceId"));
    Assert.False(errorResponse.ContainsKey("details"), "Production must not echo exception.Message to clients");
    Assert.DoesNotContain("PostgresException", responseBody);
    Assert.DoesNotContain("duplicate key", responseBody);
    Assert.DoesNotContain("user_email_key", responseBody);
  }

  [Fact]
  public async Task InvokeAsync_InStaging_AlsoRedactsExceptionMessage()
  {
    // Staging is a pre-production environment; it must behave like Production for response
    // redaction. Only Development gets the raw exception.Message in the body.
    var context = CreateHttpContext();
    var exception = new InvalidOperationException("sensitive: stripe sk_test_leak_123");

    _mockEnvironment.SetupGet(e => e.EnvironmentName).Returns("Staging");
    _mockNext.Setup(x => x(It.IsAny<HttpContext>())).ThrowsAsync(exception);

    await _middleware.InvokeAsync(context);

    var responseBody = GetResponseBody(context);
    Assert.DoesNotContain("sk_test_leak_123", responseBody);
    Assert.DoesNotContain("details", responseBody);
    Assert.Contains("traceId", responseBody);
  }

  private static HttpContext CreateHttpContext()
  {
    var context = new DefaultHttpContext();
    context.Response.Body = new MemoryStream();
    return context;
  }

  private static string GetResponseBody(HttpContext context)
  {
    context.Response.Body.Seek(0, SeekOrigin.Begin);
    using var reader = new StreamReader(context.Response.Body);
    return reader.ReadToEnd();
  }
}
