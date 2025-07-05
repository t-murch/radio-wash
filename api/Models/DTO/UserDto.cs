namespace RadioWash.Api.Models.DTO;

public class UserDto
{
  public int Id { get; set; }
  public string SupabaseId { get; set; } = null!;
  public string? DisplayName { get; set; }
  public string? Email { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}
