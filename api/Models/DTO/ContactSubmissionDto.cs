namespace RadioWash.Api.Models.DTO;

public class ContactSubmissionDto
{
  public string? Name { get; set; }
  public string? Email { get; set; }
  public string? Message { get; set; }

  // Honeypot. The real form renders this hidden and empty; any value means a bot filled it in.
  public string? Website { get; set; }
}
