using RadioWash.Api.Controllers;

namespace RadioWash.Api.Models.Domain;

public class UserMusicService
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public MusicServiceType ServiceType { get; set; }
    public string ServiceUserId { get; set; } = null!;
    public string AccessToken { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User User { get; set; } = null!;
}