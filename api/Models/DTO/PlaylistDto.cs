namespace RadioWash.Api.Models.DTO;

public class PlaylistDto
{
  public string Id { get; set; } = null!;
  public string Name { get; set; } = null!;
  public string? Description { get; set; }
  public string? ImageUrl { get; set; }
  public int TrackCount { get; set; }
  public string OwnerId { get; set; } = null!;
  public string? OwnerName { get; set; }
}
