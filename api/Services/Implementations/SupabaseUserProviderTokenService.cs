using System.Net.Http.Headers;
using System.Text.Json;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

/// <summary>
/// Implements the IUserProviderTokenService to fetch tokens from the Supabase Management API.
/// </summary>
public class SupabaseUserProviderTokenService : IUserProviderTokenService
{
  private readonly HttpClient _httpClient;
  private readonly IConfiguration _configuration;
  private readonly ILogger<SupabaseUserProviderTokenService> _logger;

  public SupabaseUserProviderTokenService(HttpClient httpClient, IConfiguration configuration, ILogger<SupabaseUserProviderTokenService> logger)
  {
    _httpClient = httpClient;
    _configuration = configuration;
    _logger = logger;
  }

  public async Task<string> GetProviderAccessTokenAsync(string supabaseUserId)
  {
    var supabaseUrl = _configuration["Supabase:Url"];
    var supabaseServiceRoleKey = _configuration["Supabase:ServiceRoleKey"];

    if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseServiceRoleKey))
    {
      _logger.LogError("Supabase URL or Service Role Key is not configured.");
      throw new InvalidOperationException("Supabase environment variables are not set.");
    }

    var requestUrl = $"{supabaseUrl}/auth/v1/admin/users/{supabaseUserId}";
    var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
    request.Headers.Add("apikey", supabaseServiceRoleKey);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supabaseServiceRoleKey);

    var response = await _httpClient.SendAsync(request);

    if (!response.IsSuccessStatusCode)
    {
      var errorContent = await response.Content.ReadAsStringAsync();
      _logger.LogError("Failed to get user from Supabase Management API. Status: {StatusCode}, Response: {Response}", response.StatusCode, errorContent);
      throw new Exception("Could not retrieve user session from Supabase.");
    }

    var jsonResponse = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(jsonResponse);
    var root = doc.RootElement;

    // Navigate through the JSON to find the spotify provider token
    if (root.TryGetProperty("identities", out var identities) && identities.ValueKind == JsonValueKind.Array)
    {
      foreach (var identity in identities.EnumerateArray())
      {
        if (identity.TryGetProperty("provider", out var provider) && provider.GetString() == "spotify")
        {
          if (identity.TryGetProperty("access_token", out var accessToken))
          {
            return accessToken.GetString() ?? throw new Exception("Spotify access token not found in identity.");
          }
        }
      }
    }

    throw new Exception("Spotify identity or access token not found for the user.");
  }
}
