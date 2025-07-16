using System.ComponentModel.DataAnnotations;

namespace RadioWash.Api.Models.Domain;

public class UserProviderData
{
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public string Provider { get; set; } = null!; // "spotify", "apple", "email"

    [Required]
    public string ProviderId { get; set; } = null!; // Provider-specific user ID

    public string? ProviderMetadata { get; set; } // JSON for provider-specific data

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
}