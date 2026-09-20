using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Controllers;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for ContactController
/// Tests validation, honeypot short-circuiting, and claim forwarding for the public
/// contact-form endpoint
/// </summary>
public class ContactControllerTests
{
  private readonly Mock<IContactService> _mockContactService;
  private readonly Mock<ILogger<ContactController>> _mockLogger;
  private readonly ContactController _controller;

  public ContactControllerTests()
  {
    _mockContactService = new Mock<IContactService>();
    _mockLogger = new Mock<ILogger<ContactController>>();

    _controller = new ContactController(_mockContactService.Object, _mockLogger.Object);
    SetupUnauthenticatedUser();
  }

  [Fact]
  public async Task Submit_WithValidRequest_ReturnsOkAndCallsService()
  {
    // Arrange
    var dto = CreateValidDto();

    // Act
    var result = await _controller.Submit(dto);

    // Assert
    Assert.IsType<OkObjectResult>(result);
    _mockContactService.Verify(
        x => x.SubmitAsync(dto, It.IsAny<string?>(), It.IsAny<string?>()),
        Times.Once);
  }

  [Theory]
  [InlineData(null, "ada@example.com", "Hello there")]
  [InlineData("", "ada@example.com", "Hello there")]
  [InlineData("Ada", null, "Hello there")]
  [InlineData("Ada", "", "Hello there")]
  [InlineData("Ada", "ada@example.com", null)]
  [InlineData("Ada", "ada@example.com", "")]
  public async Task Submit_WithMissingRequiredField_ReturnsValidationProblem(
      string? name, string? email, string? message)
  {
    // Arrange
    var dto = new ContactSubmissionDto { Name = name, Email = email, Message = message };

    // Act
    var result = await _controller.Submit(dto);

    // Assert
    AssertValidationProblem(result);
    _mockContactService.Verify(
        x => x.SubmitAsync(It.IsAny<ContactSubmissionDto>(), It.IsAny<string?>(), It.IsAny<string?>()),
        Times.Never);
  }

  [Fact]
  public async Task Submit_WithInvalidEmail_ReturnsValidationProblem()
  {
    // Arrange
    var dto = CreateValidDto();
    dto.Email = "not-an-email";

    // Act
    var result = await _controller.Submit(dto);

    // Assert
    AssertValidationProblem(result);
    _mockContactService.Verify(
        x => x.SubmitAsync(It.IsAny<ContactSubmissionDto>(), It.IsAny<string?>(), It.IsAny<string?>()),
        Times.Never);
  }

  [Fact]
  public async Task Submit_WithMessageOver5000Chars_ReturnsValidationProblem()
  {
    // Arrange
    var dto = CreateValidDto();
    dto.Message = new string('a', 5001);

    // Act
    var result = await _controller.Submit(dto);

    // Assert
    AssertValidationProblem(result);
    _mockContactService.Verify(
        x => x.SubmitAsync(It.IsAny<ContactSubmissionDto>(), It.IsAny<string?>(), It.IsAny<string?>()),
        Times.Never);
  }

  [Fact]
  public async Task Submit_WithHoneypotFilled_ReturnsOkWithoutCallingService()
  {
    // Arrange: a bot fills every field, including the hidden one. The response must be
    // indistinguishable from a real success so the sender learns nothing.
    var dto = CreateValidDto();
    dto.Website = "https://spam.example";

    // Act
    var result = await _controller.Submit(dto);

    // Assert
    Assert.IsType<OkObjectResult>(result);
    _mockContactService.Verify(
        x => x.SubmitAsync(It.IsAny<ContactSubmissionDto>(), It.IsAny<string?>(), It.IsAny<string?>()),
        Times.Never);
  }

  [Fact]
  public async Task Submit_WithAuthenticatedUser_PassesSupabaseIdToService()
  {
    // Arrange
    var userId = Guid.NewGuid();
    SetupAuthenticatedUser(userId);
    var dto = CreateValidDto();

    // Act
    var result = await _controller.Submit(dto);

    // Assert
    Assert.IsType<OkObjectResult>(result);
    _mockContactService.Verify(
        x => x.SubmitAsync(dto, It.IsAny<string?>(), userId.ToString()),
        Times.Once);
  }

  [Fact]
  public async Task Submit_WithAnonymousUser_PassesNullSupabaseId()
  {
    // Arrange
    SetupUnauthenticatedUser();
    var dto = CreateValidDto();

    // Act
    var result = await _controller.Submit(dto);

    // Assert
    Assert.IsType<OkObjectResult>(result);
    _mockContactService.Verify(
        x => x.SubmitAsync(dto, It.IsAny<string?>(), null),
        Times.Once);
  }

  private static ContactSubmissionDto CreateValidDto()
  {
    return new ContactSubmissionDto
    {
      Name = "Ada Lovelace",
      Email = "ada@example.com",
      Message = "The sync feature stopped working for me yesterday."
    };
  }

  private static void AssertValidationProblem(IActionResult result)
  {
    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    Assert.Equal("https://radiowash.com/problems/contact-validation", problem.Type);
  }

  private void SetupAuthenticatedUser(Guid userId)
  {
    var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };

    var identity = new ClaimsIdentity(claims, "TestAuthType");
    var principal = new ClaimsPrincipal(identity);

    _controller.ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext
      {
        User = principal
      }
    };
  }

  private void SetupUnauthenticatedUser()
  {
    var identity = new ClaimsIdentity();
    var principal = new ClaimsPrincipal(identity);

    _controller.ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext
      {
        User = principal
      }
    };
  }
}
