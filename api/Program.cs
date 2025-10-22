using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RadioWash.Api.Configuration;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Hubs;
using RadioWash.Api.Models.Domain;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<SpotifySettings>(builder.Configuration.GetSection(SpotifySettings.SectionName));
builder.Services.Configure<RadioWash.Api.Configuration.BatchProcessingSettings>(builder.Configuration.GetSection(RadioWash.Api.Configuration.BatchProcessingSettings.SectionName));
var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://localhost:3000";

// Services
builder.Services.AddHttpClient();

// Configure Data Protection with persistent key storage
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<RadioWashDbContext>()
    .SetApplicationName("RadioWash");
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
builder.Services.AddScoped<IMusicTokenService, MusicTokenService>();

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserProviderDataRepository, UserProviderDataRepository>();
builder.Services.AddScoped<IUserMusicTokenRepository, UserMusicTokenRepository>();
builder.Services.AddScoped<ICleanPlaylistJobRepository, CleanPlaylistJobRepository>();
builder.Services.AddScoped<ITrackMappingRepository, TrackMappingRepository>();

// Subscription repositories
builder.Services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
builder.Services.AddScoped<IUserSubscriptionRepository, UserSubscriptionRepository>();
builder.Services.AddScoped<IPlaylistSyncConfigRepository, PlaylistSyncConfigRepository>();
builder.Services.AddScoped<IPlaylistSyncHistoryRepository, PlaylistSyncHistoryRepository>();

// Services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserProviderTokenService, SupabaseUserProviderTokenService>();
builder.Services.AddScoped<ISpotifyService, SpotifyService>();
builder.Services.AddScoped<ICleanPlaylistService, CleanPlaylistService>();
builder.Services.AddScoped<IProgressBroadcastService, ProgressBroadcastService>();

// Subscription services
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IPlaylistSyncService, PlaylistSyncService>();
builder.Services.AddScoped<IPlaylistDeltaCalculator, PlaylistDeltaCalculator>();
builder.Services.AddScoped<ISyncSchedulerService, SyncSchedulerService>();
builder.Services.AddScoped<ISyncTimeCalculator, SyncTimeCalculator>();
builder.Services.AddScoped<IPaymentService, StripePaymentService>();
builder.Services.AddScoped<IEventUtility, EventUtilityWrapper>();
builder.Services.AddScoped<IStripeHealthCheckService, StripeHealthCheckService>();

// SOLID Refactored Services
builder.Services.AddScoped<RadioWash.Api.Infrastructure.Patterns.IUnitOfWork, RadioWash.Api.Infrastructure.Patterns.EntityFrameworkUnitOfWork>();
builder.Services.AddScoped<ICleanPlaylistJobProcessor, CleanPlaylistJobProcessor>();
builder.Services.AddScoped<IJobOrchestrator, HangfireJobOrchestrator>();
builder.Services.AddScoped<IPlaylistCleanerFactory, PlaylistCleanerFactory>();
builder.Services.AddScoped<SpotifyPlaylistCleaner>();
builder.Services.AddScoped<IProgressTracker, SmartProgressTracker>();
builder.Services.AddSingleton<BatchConfiguration>(provider =>
{
  var settings = builder.Configuration.GetSection(RadioWash.Api.Configuration.BatchProcessingSettings.SectionName)
      .Get<RadioWash.Api.Configuration.BatchProcessingSettings>() ?? new RadioWash.Api.Configuration.BatchProcessingSettings();
  return new BatchConfiguration(settings.BatchSize, settings.ProgressReportingThreshold, settings.DatabasePersistenceThreshold);
});

// SignalR
builder.Services.AddSignalR();

// Database
builder.Services.AddDbContext<RadioWashDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Memory Cache
builder.Services.AddMemoryCache();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(frontendUrl)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Authentication - Configure for Supabase JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test");

    var supabasePublicUrl = builder.Configuration["Supabase:PublicUrl"];
    var jwtSecret = builder.Configuration["Supabase:JwtSecret"];
    options.Authority = $"{supabasePublicUrl}/auth/v1";
    options.Audience = "authenticated";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = $"{supabasePublicUrl}/auth/v1",
        ValidAudience = "authenticated",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret!))
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // For SignalR connections, read token from query string (WebSocket/SSE cannot use headers)
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) &&
            path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
                return Task.CompletedTask;
            }

            // For regular HTTP requests, read token from Authorization header
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Bearer ") == true)
            {
                context.Token = authHeader.Substring("Bearer ".Length).Trim();
            }
            return Task.CompletedTask;
        }
    };
});

// Supabase Gotrue Client
builder.Services.AddSingleton<Supabase.Gotrue.Client>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var supabaseUrl = config["Supabase:Url"];
    var serviceRoleKey = config["Supabase:ServiceRoleKey"];
    return new Supabase.Gotrue.Client(new Supabase.Gotrue.ClientOptions
    {
        Url = $"{supabaseUrl}/auth/v1",
        Headers = new Dictionary<string, string>
        {
            ["apikey"] = serviceRoleKey!,
            ["Authorization"] = $"Bearer {serviceRoleKey}"
        }
    });
});

// Hangfire (skip in testing environments)
var skipHangfire = builder.Configuration.GetValue<bool>("SkipMigrations"); // Use same flag for consistency
if (!builder.Environment.IsEnvironment("Testing") && !builder.Environment.IsEnvironment("Test") && !skipHangfire)
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(config => config.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));
    builder.Services.AddHangfireServer();
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "RadioWash API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Sentry
if (builder.Environment.IsProduction())
{
    var sentryDsn = builder.Configuration["Sentry:Dsn"];
    if (string.IsNullOrWhiteSpace(sentryDsn))
    {
        throw new InvalidOperationException("Sentry:Dsn configuration is required for Production environment");
    }

    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsn;
        options.Environment = "Production";
        options.SampleRate = 1.0f;
        options.TracesSampleRate = 0.1f; // Performance monitoring
        options.AttachStacktrace = true;
    });
}

// Configure model state validation logging
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        var errors = context.ModelState
            .Where(x => x.Value.Errors.Count > 0)
            .Select(x => new { Field = x.Key, Errors = x.Value.Errors.Select(e => e.ErrorMessage) });

        logger.LogWarning("Model validation failed for {Method} {Path}: {@Errors}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            errors);

        return new BadRequestObjectResult(context.ModelState);
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Apply migrations (skip only for unit testing environment)
var skipMigrations = app.Configuration.GetValue<bool>("SkipMigrations");
if (!app.Environment.IsEnvironment("Testing") && !skipMigrations)
{
  using (var scope = app.Services.CreateScope())
  {
    var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
    var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
      dbContext.Database.Migrate();
      migrationLogger.LogInformation("Database migrations applied successfully");

      // Seed subscription plans
      await RadioWash.Api.Infrastructure.Data.DatabaseSeeder.SeedSubscriptionPlansAsync(dbContext, app.Configuration);
      migrationLogger.LogInformation("Database seeding completed successfully");
    }
    catch (Exception ex)
    {
      migrationLogger.LogError(ex, "Error applying database migrations or seeding");
      throw;
    }

    // Validate Stripe configuration
    var stripeHealthCheck = scope.ServiceProvider.GetRequiredService<IStripeHealthCheckService>();
    var stripeConfigValid = await stripeHealthCheck.ValidateConfigurationAsync();
    if (!stripeConfigValid)
    {
      migrationLogger.LogError("Stripe configuration validation failed - application will not start");
      throw new InvalidOperationException("Stripe configuration is invalid");
    }
    
    // Test Stripe connectivity in non-test environments
    if (!app.Environment.IsEnvironment("Testing") && !app.Environment.IsEnvironment("Test"))
    {
      var stripeConnectivityOk = await stripeHealthCheck.TestConnectivityAsync();
      if (!stripeConnectivityOk)
      {
        migrationLogger.LogWarning("Stripe connectivity test failed - check network connectivity and API keys");
      }
    }
  }
}

app.UseCors("AllowFrontend");
app.UseMiddleware<RadioWash.Api.Middleware.GlobalExceptionMiddleware>();
app.UseAuthentication();
app.UseMiddleware<RadioWash.Api.Middleware.TokenRefreshMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHub<PlaylistProgressHub>("/hubs/playlist-progress", options =>
{
    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents |
                        Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                        Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;

    // Add detailed logging for SignalR connections
    options.ApplicationMaxBufferSize = 65536;
    options.TransportMaxBufferSize = 65536;
});

// Log SignalR hub mapping
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("SignalR Hub mapped at /hubs/playlist-progress with transports: {Transports}",
    "ServerSentEvents, WebSockets, LongPolling");

// Only add Hangfire dashboard in non-testing environments
var skipHangfireDashboard = app.Configuration.GetValue<bool>("SkipMigrations"); // Use same flag for consistency
if (!app.Environment.IsEnvironment("Testing") && !app.Environment.IsEnvironment("Test") && !skipHangfireDashboard)
{
  app.UseHangfireDashboard();

  // Initialize scheduled sync jobs
  using (var scope = app.Services.CreateScope())
  {
    var syncScheduler = scope.ServiceProvider.GetRequiredService<ISyncSchedulerService>();
    syncScheduler.InitializeScheduledJobs();
  }
}

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
