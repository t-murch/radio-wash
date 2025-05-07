namespace RadioWash.Api.Models.DTO;

public class UserDto
{
  public int Id { get; set; }
  public string SpotifyId { get; set; } = null!;
  public string DisplayName { get; set; } = null!;
  public string Email { get; set; } = null!;
  public string? ProfileImageUrl { get; set; }
}
