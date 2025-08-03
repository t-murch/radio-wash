namespace RadioWash.Api.Models.DTO;

public class CreateCleanPlaylistJobDto
{
  public string SourcePlaylistId { get; set; } = null!;
  public string? TargetPlaylistName { get; set; }
}