using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadioWash.Api.Controllers;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Tests.Unit.Controllers;

public class CleanPlaylistControllerTests : IDisposable
{
  private readonly Mock<ICleanPlaylistService> _mockCleanPlaylistService;
  private readonly Mock<ILogger<CleanPlaylistController>> _mockLogger;
  private readonly RadioWashDbContext _context;
  private readonly CleanPlaylistController _controller;

  public CleanPlaylistControllerTests()
  {
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;

    _context = new RadioWashDbContext(options);
    _mockCleanPlaylistService = new Mock<ICleanPlaylistService>();
    _mockLogger = new Mock<ILogger<CleanPlaylistController>>();

    _controller = new CleanPlaylistController(
      _mockCleanPlaylistService.Object,
      _context,
      _mockLogger.Object);

    SetupAuthenticatedUser();
  }

  public void Dispose()
  {
    _context.Dispose();
  }

  [Fact]
  public async Task CreateCleanPlaylistJob_WithUnsupportedProvider_ReturnsBadRequest()
  {
    var jobDto = new CreateCleanPlaylistJobDto
    {
      SourcePlaylistId = "playlist-1",
      Provider = "apple_music"
    };

    _mockCleanPlaylistService
      .Setup(x => x.CreateJobAsync(1, jobDto))
      .ThrowsAsync(new ArgumentException("Provider 'apple_music' is not supported."));

    var result = await _controller.CreateCleanPlaylistJob(1, jobDto);

    var badRequest = Assert.IsType<BadRequestObjectResult>(result);
    var response = badRequest.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal("Provider 'apple_music' is not supported.", errorProperty?.GetValue(response));
  }

  private void SetupAuthenticatedUser()
  {
    _context.Users.Add(new User
    {
      Id = 1,
      SupabaseId = "test-supabase-id",
      DisplayName = "Test User",
      Email = "test@example.com",
      CreatedAt = DateTime.UtcNow
    });
    _context.SaveChanges();

    var claims = new List<Claim>
    {
      new(ClaimTypes.NameIdentifier, "test-supabase-id")
    };

    _controller.ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext
      {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
      }
    };
  }
}
