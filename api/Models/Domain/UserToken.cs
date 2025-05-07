namespace RadioWash.Api.Models.Domain;

public class UserToken
{
  public int Id { get; set; }
  public int UserId { get; set; }
  public string AccessToken { get; set; } = null!;
  public string RefreshToken { get; set; } = null!;
  public DateTime ExpiresAt { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  public User User { get; set; } = null!;
}
