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
using System.Security.Claims;
using Xunit;

namespace RadioWash.Api.Tests.Unit.Controllers;

public class PlaylistSyncControllerTests : IDisposable
{
  private readonly Mock<IPlaylistSyncService> _mockSyncService;
  private readonly Mock<ISubscriptionService> _mockSubscriptionService;
  private readonly Mock<ILogger<PlaylistSyncController>> _mockLogger;
  private readonly RadioWashDbContext _context;
  private readonly PlaylistSyncController _controller;

  public PlaylistSyncControllerTests()
  {
    var options = new DbContextOptionsBuilder<RadioWashDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;
    _context = new RadioWashDbContext(options);

    _mockSyncService = new Mock<IPlaylistSyncService>();
    _mockSubscriptionService = new Mock<ISubscriptionService>();
    _mockLogger = new Mock<ILogger<PlaylistSyncController>>();

    _controller = new PlaylistSyncController(
        _context,
        _mockSyncService.Object,
        _mockSubscriptionService.Object,
        _mockLogger.Object
    );

    // Setup authenticated user context
    SetupAuthenticatedUser();
  }

  public void Dispose()
  {
    _context.Dispose();
  }

  private void SetupAuthenticatedUser()
  {
    var user = new User
    {
      Id = 1,
      SupabaseId = "test-supabase-id",
      DisplayName = "Test User",
      Email = "test@example.com",
      CreatedAt = DateTime.UtcNow
    };
    _context.Users.Add(user);
    _context.SaveChanges();

    var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "test-supabase-id")
        };
    var identity = new ClaimsIdentity(claims, "TestAuth");
    var principal = new ClaimsPrincipal(identity);

    _controller.ControllerContext = new ControllerContext()
    {
      HttpContext = new DefaultHttpContext() { User = principal }
    };
  }

  [Fact]
  public async Task GetSyncConfigs_ShouldReturnOkWithConfigs()
  {
    // Arrange
    var configs = new List<PlaylistSyncConfig>
        {
            CreatePlaylistSyncConfig(1, 1),
            CreatePlaylistSyncConfig(2, 2)
        };
    _mockSyncService.Setup(x => x.GetUserSyncConfigsAsync(1))
        .ReturnsAsync(configs);

    // Act
    var result = await _controller.GetSyncConfigs();

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var returnedConfigs = Assert.IsAssignableFrom<IEnumerable<PlaylistSyncConfigDto>>(okResult.Value);
    Assert.Equal(2, returnedConfigs.Count());
  }

  [Fact]
  public async Task EnableSync_WithActiveSubscription_ShouldReturnOkWithConfig()
  {
    // Arrange
    var enableSyncDto = new EnableSyncDto { JobId = 1 };
    var config = CreatePlaylistSyncConfig(1, 1);

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(true);
    _mockSyncService.Setup(x => x.EnableSyncForJobAsync(1, 1))
        .ReturnsAsync(config);

    // Act
    var result = await _controller.EnableSync(enableSyncDto);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var returnedConfig = Assert.IsType<PlaylistSyncConfigDto>(okResult.Value);
    Assert.Equal(1, returnedConfig.Id);
    Assert.True(returnedConfig.IsActive);
  }

  [Fact]
  public async Task EnableSync_WithoutActiveSubscription_ShouldReturnBadRequest()
  {
    // Arrange
    var enableSyncDto = new EnableSyncDto { JobId = 1 };

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(false);

    // Act
    var result = await _controller.EnableSync(enableSyncDto);

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
    var response = badRequestResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal("Active subscription required to enable sync", errorProperty?.GetValue(response));
  }

  [Fact]
  public async Task EnableSync_WhenServiceReturnsNull_ShouldReturnBadRequest()
  {
    // Arrange
    var enableSyncDto = new EnableSyncDto { JobId = 1 };

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(true);
    _mockSyncService.Setup(x => x.EnableSyncForJobAsync(1, 1))
        .ReturnsAsync((PlaylistSyncConfig?)null);

    // Act
    var result = await _controller.EnableSync(enableSyncDto);

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
    var response = badRequestResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal("Failed to enable sync", errorProperty?.GetValue(response));
  }

  [Fact]
  public async Task EnableSync_WhenServiceThrows_ShouldReturnBadRequest()
  {
    // Arrange
    var enableSyncDto = new EnableSyncDto { JobId = 1 };
    var exceptionMessage = "Service error occurred";

    _mockSubscriptionService.Setup(x => x.HasActiveSubscriptionAsync(1))
        .ReturnsAsync(true);
    _mockSyncService.Setup(x => x.EnableSyncForJobAsync(1, 1))
        .ThrowsAsync(new Exception(exceptionMessage));

    // Act
    var result = await _controller.EnableSync(enableSyncDto);

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
    var response = badRequestResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal(exceptionMessage, errorProperty?.GetValue(response));
  }

  [Fact]
  public async Task DisableSync_WithValidConfigId_ShouldReturnOk()
  {
    // Arrange
    var configId = 1;
    _mockSyncService.Setup(x => x.DisableSyncAsync(configId, 1))
        .ReturnsAsync(true);

    // Act
    var result = await _controller.DisableSync(configId);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result);
    var response = okResult.Value;
    Assert.NotNull(response);
    var messageProperty = response.GetType().GetProperty("message");
    Assert.Equal("Sync disabled successfully", messageProperty?.GetValue(response));
  }

  [Fact]
  public async Task DisableSync_WhenConfigNotFound_ShouldReturnNotFound()
  {
    // Arrange
    var configId = 999;
    _mockSyncService.Setup(x => x.DisableSyncAsync(configId, 1))
        .ReturnsAsync(false);

    // Act
    var result = await _controller.DisableSync(configId);

    // Assert
    var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
    var response = notFoundResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal("Sync configuration not found", errorProperty?.GetValue(response));
  }

  [Fact]
  public async Task UpdateSyncFrequency_WithValidData_ShouldReturnOkWithConfig()
  {
    // Arrange
    var configId = 1;
    var updateDto = new UpdateSyncFrequencyDto { Frequency = "weekly" };
    var updatedConfig = CreatePlaylistSyncConfig(configId, 1, "weekly");

    _mockSyncService.Setup(x => x.UpdateSyncFrequencyAsync(configId, "weekly", 1))
        .ReturnsAsync(updatedConfig);

    // Act
    var result = await _controller.UpdateSyncFrequency(configId, updateDto);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var returnedConfig = Assert.IsType<PlaylistSyncConfigDto>(okResult.Value);
    Assert.Equal("weekly", returnedConfig.SyncFrequency);
  }

  [Fact]
  public async Task UpdateSyncFrequency_WhenConfigNotFound_ShouldReturnNotFound()
  {
    // Arrange
    var configId = 999;
    var updateDto = new UpdateSyncFrequencyDto { Frequency = "weekly" };

    _mockSyncService.Setup(x => x.UpdateSyncFrequencyAsync(configId, "weekly", 1))
        .ReturnsAsync((PlaylistSyncConfig?)null);

    // Act
    var result = await _controller.UpdateSyncFrequency(configId, updateDto);

    // Assert
    var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
    var response = notFoundResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal("Sync configuration not found", errorProperty?.GetValue(response));
  }

  [Fact]
  public async Task ManualSync_WithValidConfigId_ShouldReturnOkWithResult()
  {
    // Arrange
    var configId = 1;
    var syncResult = new PlaylistSyncResult
    {
      Success = true,
      TracksAdded = 5,
      TracksRemoved = 2,
      TracksUnchanged = 10,
      ErrorMessage = null,
      ExecutionTime = TimeSpan.FromMilliseconds(1500)
    };

    _mockSyncService.Setup(x => x.ManualSyncAsync(configId, 1))
        .ReturnsAsync(syncResult);

    // Act
    var result = await _controller.ManualSync(configId);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var returnedResult = Assert.IsType<SyncResultDto>(okResult.Value);
    Assert.True(returnedResult.Success);
    Assert.Equal(5, returnedResult.TracksAdded);
    Assert.Equal(2, returnedResult.TracksRemoved);
    Assert.Equal(10, returnedResult.TracksUnchanged);
    Assert.Equal(1500, returnedResult.ExecutionTimeMs);
  }

  [Fact]
  public async Task ManualSync_WhenServiceThrows_ShouldReturnBadRequest()
  {
    // Arrange
    var configId = 1;
    var exceptionMessage = "Sync failed";

    _mockSyncService.Setup(x => x.ManualSyncAsync(configId, 1))
        .ThrowsAsync(new Exception(exceptionMessage));

    // Act
    var result = await _controller.ManualSync(configId);

    // Assert
    var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
    var response = badRequestResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal(exceptionMessage, errorProperty?.GetValue(response));
  }

  [Fact]
  public async Task GetSyncHistory_WithValidConfigId_ShouldReturnOkWithHistory()
  {
    // Arrange
    var configId = 1;
    var configs = new List<PlaylistSyncConfig> { CreatePlaylistSyncConfig(configId, 1) };
    var history = new List<PlaylistSyncHistory>
        {
            CreateSyncHistory(1, configId),
            CreateSyncHistory(2, configId)
        };

    _mockSyncService.Setup(x => x.GetUserSyncConfigsAsync(1))
        .ReturnsAsync(configs);
    _mockSyncService.Setup(x => x.GetSyncHistoryAsync(configId, 20))
        .ReturnsAsync(history);

    // Act
    var result = await _controller.GetSyncHistory(configId);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var returnedHistory = Assert.IsAssignableFrom<IEnumerable<PlaylistSyncHistoryDto>>(okResult.Value);
    Assert.Equal(2, returnedHistory.Count());
  }

  [Fact]
  public async Task GetSyncHistory_WithCustomLimit_ShouldReturnOkWithHistory()
  {
    // Arrange
    var configId = 1;
    var limit = 5;
    var configs = new List<PlaylistSyncConfig> { CreatePlaylistSyncConfig(configId, 1) };
    var history = new List<PlaylistSyncHistory>
        {
            CreateSyncHistory(1, configId),
            CreateSyncHistory(2, configId)
        };

    _mockSyncService.Setup(x => x.GetUserSyncConfigsAsync(1))
        .ReturnsAsync(configs);
    _mockSyncService.Setup(x => x.GetSyncHistoryAsync(configId, limit))
        .ReturnsAsync(history);

    // Act
    var result = await _controller.GetSyncHistory(configId, limit);

    // Assert
    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    var returnedHistory = Assert.IsAssignableFrom<IEnumerable<PlaylistSyncHistoryDto>>(okResult.Value);
    Assert.Equal(2, returnedHistory.Count());
  }

  [Fact]
  public async Task GetSyncHistory_WhenUserDoesNotOwnConfig_ShouldReturnNotFound()
  {
    // Arrange
    var configId = 999;
    var configs = new List<PlaylistSyncConfig> { CreatePlaylistSyncConfig(1, 1) }; // Config 1, not 999

    _mockSyncService.Setup(x => x.GetUserSyncConfigsAsync(1))
        .ReturnsAsync(configs);

    // Act
    var result = await _controller.GetSyncHistory(configId);

    // Assert
    var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
    var response = notFoundResult.Value;
    Assert.NotNull(response);
    var errorProperty = response.GetType().GetProperty("error");
    Assert.Equal("Sync configuration not found", errorProperty?.GetValue(response));
  }

  private static PlaylistSyncConfig CreatePlaylistSyncConfig(int id, int originalJobId, string frequency = "daily")
  {
    return new PlaylistSyncConfig
    {
      Id = id,
      OriginalJobId = originalJobId,
      SourcePlaylistId = "source_playlist_123",
      TargetPlaylistId = "target_playlist_456",
      IsActive = true,
      SyncFrequency = frequency,
      LastSyncedAt = DateTime.UtcNow.AddHours(-1),
      LastSyncStatus = "success",
      LastSyncError = null,
      NextScheduledSync = DateTime.UtcNow.AddDays(1),
      CreatedAt = DateTime.UtcNow.AddDays(-7),
      UpdatedAt = DateTime.UtcNow,
      OriginalJob = new CleanPlaylistJob
      {
        Id = originalJobId,
        SourcePlaylistName = "Source Playlist",
        TargetPlaylistName = "Target Playlist",
        UserId = 1,
        CreatedAt = DateTime.UtcNow.AddDays(-7)
      }
    };
  }

  private static PlaylistSyncHistory CreateSyncHistory(int id, int syncConfigId)
  {
    return new PlaylistSyncHistory
    {
      Id = id,
      SyncConfigId = syncConfigId,
      StartedAt = DateTime.UtcNow.AddHours(-2),
      CompletedAt = DateTime.UtcNow.AddHours(-2).AddMinutes(5),
      Status = "success",
      TracksAdded = 3,
      TracksRemoved = 1,
      TracksUnchanged = 15,
      ErrorMessage = null,
      ExecutionTimeMs = 5000
    };
  }
}
