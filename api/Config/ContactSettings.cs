namespace RadioWash.Api.Configuration;

public class ContactSettings
{
  public const string SectionName = "Contact";

  // Where contact-form submissions are delivered. No default on purpose: the owner's inbox
  // is deployment configuration, and a hardcoded fallback would silently mail the wrong place.
  public string? RecipientEmail { get; set; }

  // Must be on the Resend-verified sending domain (updates.radiowash.com — see
  // docs/ops/launch-checklist.md). Display-name form is what Resend expects.
  public string FromEmail { get; set; } = "RadioWash Contact <contact@updates.radiowash.com>";
}
