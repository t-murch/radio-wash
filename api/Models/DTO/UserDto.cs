namespace RadioWash.Api.Models.DTO;

public class UserDto
{
  public int Id { get; set; }
  public string SupabaseId { get; set; } = null!;
  public string? DisplayName { get; set; }
  public string? Email { get; set; }
  public string? PrimaryProvider { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
  public ICollection<UserProviderDataDto> ProviderData { get; set; } = new List<UserProviderDataDto>();
}

public class UserProviderDataDto
{
  public int Id { get; set; }
  public string Provider { get; set; } = null!;
  public string ProviderId { get; set; } = null!;
  public string? ProviderMetadata { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}
