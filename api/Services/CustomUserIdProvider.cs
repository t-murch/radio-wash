using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace RadioWash.Api.Services;

public class CustomUserIdProvider : IUserIdProvider
{
  public string? GetUserId(HubConnectionContext connection)
  {
    // Get user ID from JWT sub claim
    return connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
  }
}
