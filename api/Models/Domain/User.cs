using System.ComponentModel.DataAnnotations;

namespace RadioWash.Api.Models.Domain;

public class User
{
  public int Id { get; set; }

  // This now stores the Supabase User ID (UUID), which is the primary identifier.
  [Required]
  public string SupabaseId { get; set; } = null!;

  [Required]
  public string DisplayName { get; set; } = null!;

  [Required]
  public string Email { get; set; } = null!;

  // Primary authentication provider (null for email/password only)
  public string? PrimaryProvider { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  // The UserToken is no longer needed here, as Supabase manages the provider tokens.
  public ICollection<CleanPlaylistJob> Jobs { get; set; } = new List<CleanPlaylistJob>();
  public ICollection<UserProviderData> ProviderData { get; set; } = new List<UserProviderData>();
}
