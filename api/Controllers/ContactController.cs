using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Controllers;

/// <summary>
/// Public contact form endpoint. Extends ControllerBase directly (not
/// AuthenticatedControllerBase) because anonymous visitors are the point; when a bearer token
/// is present anyway, the Supabase user id is recorded on the submission for support triage.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ContactController : ControllerBase
{
  private const int NameMaxLength = 200;
  private const int EmailMaxLength = 320;
  private const int MessageMaxLength = 5000;

  private readonly IContactService _contactService;
  private readonly ILogger<ContactController> _logger;

  public ContactController(IContactService contactService, ILogger<ContactController> logger)
  {
    _contactService = contactService;
    _logger = logger;
  }

  [HttpPost]
  [AllowAnonymous]
  [EnableRateLimiting("contact")]
  public async Task<IActionResult> Submit([FromBody] ContactSubmissionDto dto)
  {
    // Honeypot: the real form ships this field hidden and empty. A value means a bot filled
    // it in — answer with the normal success body so the sender learns nothing, and persist
    // nothing so known-bot payloads don't accumulate.
    if (!string.IsNullOrWhiteSpace(dto.Website))
    {
      _logger.LogWarning(
          "Contact honeypot triggered from {ClientIp}",
          HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
      return Ok(new { status = "received" });
    }

    var validationError = Validate(dto);
    if (validationError != null)
    {
      return Problem(
          title: "Invalid contact submission",
          detail: validationError,
          statusCode: StatusCodes.Status400BadRequest,
          type: "https://radiowash.com/problems/contact-validation");
    }

    var supabaseUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;
    var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

    try
    {
      await _contactService.SubmitAsync(dto, clientIp, supabaseUserId);
      return Ok(new { status = "received" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to store contact submission");
      return Problem(
          title: "Submission failed",
          detail: "Something went wrong while sending your message. Please try again.",
          statusCode: StatusCodes.Status500InternalServerError);
    }
  }

  private static string? Validate(ContactSubmissionDto dto)
  {
    var name = dto.Name?.Trim();
    if (string.IsNullOrEmpty(name))
    {
      return "Name is required.";
    }
    if (name.Length > NameMaxLength)
    {
      return $"Name must be {NameMaxLength} characters or fewer.";
    }

    var email = dto.Email?.Trim();
    if (string.IsNullOrEmpty(email))
    {
      return "Email is required.";
    }
    if (email.Length > EmailMaxLength || !IsValidEmail(email))
    {
      return "Email must be a valid email address.";
    }

    var message = dto.Message?.Trim();
    if (string.IsNullOrEmpty(message))
    {
      return "Message is required.";
    }
    if (message.Length > MessageMaxLength)
    {
      return $"Message must be {MessageMaxLength} characters or fewer.";
    }

    return null;
  }

  private static bool IsValidEmail(string email)
  {
    try
    {
      // MailAddress accepts display-name forms like "a <a@b.co>"; requiring the parsed
      // address to round-trip rejects those and keeps only a bare address.
      return new System.Net.Mail.MailAddress(email).Address == email;
    }
    catch (FormatException)
    {
      return false;
    }
  }
}
