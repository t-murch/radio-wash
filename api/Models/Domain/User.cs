namespace RadioWash.Api.Models.Domain;

public class User
{
  public int Id { get; set; }
  public string SpotifyId { get; set; } = null!;
  public string DisplayName { get; set; } = null!;
  public string Email { get; set; } = null!;
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  // Navigation properties
  public UserToken? Token { get; set; }
  public ICollection<CleanPlaylistJob> Jobs { get; set; } = new List<CleanPlaylistJob>();
}

