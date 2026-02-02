using System.Net.Http.Json;

namespace RadioWash.Api.Tests.Integration.TestHelpers;

/// <summary>
/// Test fixture that connects to the locally running Supabase stack (started via `supabase start`).
/// This provides maximum fidelity with the development environment and avoids container startup overhead.
///
/// Prerequisites:
/// - Run `supabase start` before running tests
/// - Supabase should be available at http://127.0.0.1:54321
/// </summary>
public class LocalSupabaseTestFixture : IAsyncLifetime
{
    private const string SupabaseUrlEnvVar = "SUPABASE_TEST_URL";
    private const string DefaultSupabaseUrl = "http://127.0.0.1:54321";

    // Default local Supabase credentials (from `supabase status -o env`)
    private const string DefaultJwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";
    private const string DefaultAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9.CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0";
    private const string DefaultServiceRoleKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU";

    public string SupabaseUrl { get; private set; } = DefaultSupabaseUrl;
    public string AuthUrl => $"{SupabaseUrl}/auth/v1";
    public string DatabaseConnectionString => "Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres";
    public string JwtSecret => DefaultJwtSecret;
    public string AnonKey => DefaultAnonKey;
    public string ServiceRoleKey => DefaultServiceRoleKey;

    /// <summary>
    /// The issuer URL that matches what local GoTrue uses in JWTs.
    /// </summary>
    public string JwtIssuer => $"{SupabaseUrl}/auth/v1";

    public async Task InitializeAsync()
    {
        // Check if custom Supabase URL is provided via environment variable
        var customUrl = Environment.GetEnvironmentVariable(SupabaseUrlEnvVar);
        if (!string.IsNullOrEmpty(customUrl))
        {
            SupabaseUrl = customUrl;
        }

        // Verify Supabase is running
        await VerifySupabaseIsRunningAsync();
    }

    private async Task VerifySupabaseIsRunningAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            var response = await client.GetAsync($"{AuthUrl}/health");
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Supabase auth service is not healthy. Status: {response.StatusCode}. " +
                    "Make sure to run 'supabase start' before running integration tests.");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Cannot connect to Supabase at {SupabaseUrl}. " +
                "Make sure to run 'supabase start' before running integration tests. " +
                $"Error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException(
                $"Connection to Supabase at {SupabaseUrl} timed out. " +
                "Make sure to run 'supabase start' before running integration tests.");
        }
    }

    /// <summary>
    /// Creates a test user via GoTrue API and returns the authentication response.
    /// </summary>
    public async Task<AuthResponse> CreateTestUserAsync(string email, string password)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("apikey", AnonKey);

        var response = await client.PostAsJsonAsync($"{AuthUrl}/signup", new
        {
            email,
            password
        });

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to create test user: {response.StatusCode} - {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return result ?? throw new InvalidOperationException("Failed to parse auth response");
    }

    /// <summary>
    /// Signs in an existing user and returns fresh tokens.
    /// </summary>
    public async Task<AuthResponse> SignInAsync(string email, string password)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("apikey", AnonKey);

        var response = await client.PostAsJsonAsync($"{AuthUrl}/token?grant_type=password", new
        {
            email,
            password
        });

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to sign in: {response.StatusCode} - {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return result ?? throw new InvalidOperationException("Failed to parse auth response");
    }

    /// <summary>
    /// Deletes a test user using the service role key (admin operation).
    /// </summary>
    public async Task DeleteTestUserAsync(string userId)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("apikey", ServiceRoleKey);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ServiceRoleKey}");

        var response = await client.DeleteAsync($"{AuthUrl}/admin/users/{userId}");

        // Ignore 404 - user might already be deleted
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to delete test user: {response.StatusCode} - {error}");
        }
    }

    public Task DisposeAsync()
    {
        // No cleanup needed - we're using the existing Supabase instance
        return Task.CompletedTask;
    }

    public record AuthResponse(
        string? access_token,
        string? token_type,
        int? expires_in,
        string? refresh_token,
        UserInfo? user
    );

    public record UserInfo(
        string id,
        string? aud,
        string? role,
        string? email,
        string? phone,
        DateTime? confirmed_at,
        DateTime? last_sign_in_at,
        Dictionary<string, object>? app_metadata,
        Dictionary<string, object>? user_metadata,
        DateTime created_at,
        DateTime updated_at
    );
}
