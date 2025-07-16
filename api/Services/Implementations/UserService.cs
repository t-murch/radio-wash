using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Models.DTO;
using RadioWash.Api.Services.Interfaces;

namespace RadioWash.Api.Services.Implementations;

public class UserService : IUserService
{
    private readonly RadioWashDbContext _dbContext;
    private readonly ILogger<UserService> _logger;

    public UserService(RadioWashDbContext dbContext, ILogger<UserService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<UserDto?> GetUserBySupabaseIdAsync(Guid supabaseId)
    {
        try
        {
            var user = await _dbContext.Users
                .Include(u => u.ProviderData)
                .FirstOrDefaultAsync(u => u.SupabaseId == supabaseId.ToString());

            if (user == null)
            {
                _logger.LogInformation("User not found for Supabase ID: {SupabaseId}", supabaseId);
                return null;
            }

            return MapToDto(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user by Supabase ID: {SupabaseId}", supabaseId);
            throw;
        }
    }

    public async Task<UserDto?> GetUserByEmailAsync(string email)
    {
        try
        {
            var user = await _dbContext.Users
                .Include(u => u.ProviderData)
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                _logger.LogInformation("User not found for email: {Email}", email);
                return null;
            }

            return MapToDto(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user by email: {Email}", email);
            throw;
        }
    }

    public async Task<UserDto?> GetUserByProviderAsync(string provider, string providerId)
    {
        try
        {
            var userProviderData = await _dbContext.Set<UserProviderData>()
                .Include(upd => upd.User)
                .ThenInclude(u => u.ProviderData)
                .FirstOrDefaultAsync(upd => upd.Provider == provider && upd.ProviderId == providerId);

            if (userProviderData?.User == null)
            {
                _logger.LogInformation("User not found for provider: {Provider}, ID: {ProviderId}", provider, providerId);
                return null;
            }

            return MapToDto(userProviderData.User);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user by provider: {Provider}, ID: {ProviderId}", provider, providerId);
            throw;
        }
    }

    public async Task<UserDto> CreateUserAsync(string supabaseId, string displayName, string email, string? primaryProvider = null)
    {
        try
        {
            var user = new User
            {
                SupabaseId = supabaseId,
                DisplayName = displayName,
                Email = email,
                PrimaryProvider = primaryProvider,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Created new user with Supabase ID: {SupabaseId}, Primary Provider: {Provider}", 
                supabaseId, primaryProvider ?? "email");

            return MapToDto(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user with Supabase ID: {SupabaseId}", supabaseId);
            throw;
        }
    }

    public async Task<UserDto> UpdateUserAsync(int userId, string? displayName = null, string? email = null)
    {
        try
        {
            var user = await _dbContext.Users
                .Include(u => u.ProviderData)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                throw new KeyNotFoundException($"User with ID {userId} not found");
            }

            if (!string.IsNullOrEmpty(displayName))
            {
                user.DisplayName = displayName;
            }

            if (!string.IsNullOrEmpty(email))
            {
                user.Email = email;
            }

            user.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Updated user with ID: {UserId}", userId);

            return MapToDto(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user with ID: {UserId}", userId);
            throw;
        }
    }

    public async Task<UserDto> LinkProviderAsync(string supabaseId, string provider, string providerId, object? providerData = null)
    {
        try
        {
            var user = await _dbContext.Users
                .Include(u => u.ProviderData)
                .FirstOrDefaultAsync(u => u.SupabaseId == supabaseId);

            if (user == null)
            {
                throw new KeyNotFoundException($"User with Supabase ID {supabaseId} not found");
            }

            // Check if provider is already linked
            var existingProviderData = user.ProviderData
                .FirstOrDefault(pd => pd.Provider == provider);

            if (existingProviderData != null)
            {
                // Update existing provider data
                existingProviderData.ProviderId = providerId;
                existingProviderData.ProviderMetadata = providerData != null ? JsonConvert.SerializeObject(providerData) : null;
                existingProviderData.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // Add new provider data
                var newProviderData = new UserProviderData
                {
                    UserId = user.Id,
                    Provider = provider,
                    ProviderId = providerId,
                    ProviderMetadata = providerData != null ? JsonConvert.SerializeObject(providerData) : null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                user.ProviderData.Add(newProviderData);
            }

            // Set as primary provider if this is the first provider or if explicitly requested
            if (user.PrimaryProvider == null || user.ProviderData.Count == 1)
            {
                user.PrimaryProvider = provider;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Linked provider {Provider} to user {SupabaseId}", provider, supabaseId);

            return MapToDto(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error linking provider {Provider} to user {SupabaseId}", provider, supabaseId);
            throw;
        }
    }

    public async Task<UserDto> SetPrimaryProviderAsync(string supabaseId, string provider)
    {
        try
        {
            var user = await _dbContext.Users
                .Include(u => u.ProviderData)
                .FirstOrDefaultAsync(u => u.SupabaseId == supabaseId);

            if (user == null)
            {
                throw new KeyNotFoundException($"User with Supabase ID {supabaseId} not found");
            }

            // Verify the provider is linked
            if (!user.ProviderData.Any(pd => pd.Provider == provider))
            {
                throw new ArgumentException($"Provider {provider} is not linked to user {supabaseId}");
            }

            user.PrimaryProvider = provider;
            user.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Set primary provider to {Provider} for user {SupabaseId}", provider, supabaseId);

            return MapToDto(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting primary provider {Provider} for user {SupabaseId}", provider, supabaseId);
            throw;
        }
    }

    public async Task<bool> HasProviderLinkedAsync(string supabaseId, string provider)
    {
        try
        {
            return await _dbContext.Users
                .Where(u => u.SupabaseId == supabaseId)
                .SelectMany(u => u.ProviderData)
                .AnyAsync(pd => pd.Provider == provider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if provider {Provider} is linked for user {SupabaseId}", provider, supabaseId);
            throw;
        }
    }

    public async Task<IEnumerable<string>> GetLinkedProvidersAsync(string supabaseId)
    {
        try
        {
            return await _dbContext.Users
                .Where(u => u.SupabaseId == supabaseId)
                .SelectMany(u => u.ProviderData)
                .Select(pd => pd.Provider)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting linked providers for user {SupabaseId}", supabaseId);
            throw;
        }
    }

    private static UserDto MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            SupabaseId = user.SupabaseId,
            DisplayName = user.DisplayName,
            Email = user.Email,
            PrimaryProvider = user.PrimaryProvider,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            ProviderData = user.ProviderData.Select(pd => new UserProviderDataDto
            {
                Id = pd.Id,
                Provider = pd.Provider,
                ProviderId = pd.ProviderId,
                ProviderMetadata = pd.ProviderMetadata,
                CreatedAt = pd.CreatedAt,
                UpdatedAt = pd.UpdatedAt
            }).ToList()
        };
    }
}