namespace RadioWash.Api.Models.DTO;

public class UserDto
{
  public Guid Id { get; set; }
  public string SpotifyId { get; set; } = null!;
  public string? DisplayName { get; set; }
  public string? Email { get; set; }
  public string? ProfileImageUrl { get; set; }
}
