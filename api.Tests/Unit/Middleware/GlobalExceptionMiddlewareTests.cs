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
  private readonly GlobalExceptionMiddleware _middleware;

  public GlobalExceptionMiddlewareTests()
  {
    _mockLogger = new Mock<ILogger<GlobalExceptionMiddleware>>();
    _mockNext = new Mock<RequestDelegate>();
    _middleware = new GlobalExceptionMiddleware(_mockNext.Object, _mockLogger.Object);
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

    // Assert
    var responseBody = GetResponseBody(context);
    var errorResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

    Assert.NotNull(errorResponse);
    Assert.True(errorResponse.ContainsKey("error"));
    Assert.True(errorResponse.ContainsKey("details"));
    Assert.Equal(2, errorResponse.Count);
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
