namespace RadioWash.Api.Models.DTO;

public class UserDto
{
  public int Id { get; set; }
  public Guid SupabaseUserId { get; set; }
  public string? SpotifyId { get; set; }
  public string DisplayName { get; set; } = null!;
  public string Email { get; set; } = null!;
  public string? ProfileImageUrl { get; set; }
}
