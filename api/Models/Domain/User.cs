namespace RadioWash.Api.Models.Domain;

public class User
{
  public Guid Id { get; set; }
  public Guid SupabaseUserId { get; set; }
  public string SpotifyId { get; set; } = null!;
  public string? Email { get; set; }
  public string? DisplayName { get; set; }
  public string EncryptedSpotifyAccessToken { get; set; } = null!;
  public string EncryptedSpotifyRefreshToken { get; set; } = null!;
  public DateTime SpotifyTokenExpiresAt { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  public ICollection<CleanPlaylistJob> Jobs { get; set; } = new List<CleanPlaylistJob>();
}

