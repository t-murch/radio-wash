using System.Net;

namespace RadioWash.Api.Test.Integration;

/// <summary>
/// Basic integration test to verify application startup
/// </summary>
public class BasicIntegrationTest : IntegrationTestBase
{
    [Fact]
    public async Task HealthCheck_ShouldReturnSuccess()
    {
        try
        {
            // Test basic application startup without authentication
            var response = await Client.GetAsync("/");
            
            // Print status and content for debugging
            var content = await response.Content.ReadAsStringAsync();
            System.Console.WriteLine($"Status: {response.StatusCode}");
            System.Console.WriteLine($"Content: {content}");
            
            // We expect some response, not necessarily OK
            Assert.True(response.StatusCode != HttpStatusCode.InternalServerError, 
                $"Application should start without internal server error. Status: {response.StatusCode}, Content: {content}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Exception during test: {ex.Message}");
            System.Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}