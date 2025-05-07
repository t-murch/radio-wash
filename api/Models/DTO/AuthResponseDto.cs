namespace RadioWash.Api.Models.DTO;

public class AuthResponseDto
{
  public string Token { get; set; } = null!;
  public UserDto User { get; set; } = null!;
}
