using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RadioWash.Api.Services.Interfaces;
using System.Security.Claims;

namespace RadioWash.Api.Attributes;

/// <summary>
/// Authorization attribute that requires users to have at least one active music service
/// before accessing protected endpoints
/// </summary>
public class RequiresMusicServiceAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Get the current user's Supabase ID from claims
        var userIdClaim = context.HttpContext.User.FindFirst("sub");
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid user token" });
            return;
        }

        // Get the music service auth service from DI
        var musicServiceAuthService = context.HttpContext.RequestServices
            .GetRequiredService<IMusicServiceAuthService>();

        try
        {
            // Check if user has any active music services
            var connectedServices = await musicServiceAuthService.GetConnectedServicesAsync(userId);
            var hasValidService = connectedServices.Any(service => 
                service.IsActive && service.ExpiresAt > DateTime.UtcNow);

            if (!hasValidService)
            {
                // Return a specific response indicating music service setup is required
                context.Result = new ObjectResult(new 
                { 
                    error = "Music service required",
                    message = "You must connect at least one music service (Spotify or Apple Music) to access this feature.",
                    requiresMusicServiceSetup = true
                })
                {
                    StatusCode = 403 // Forbidden - user is authenticated but lacks required permissions
                };
                return;
            }
        }
        catch (Exception)
        {
            // If we can't check music services, err on the side of caution
            context.Result = new ObjectResult(new 
            { 
                error = "Unable to verify music service status",
                message = "Please ensure you have connected a music service to access this feature.",
                requiresMusicServiceSetup = true
            })
            {
                StatusCode = 503 // Service Unavailable
            };
        }
    }
}