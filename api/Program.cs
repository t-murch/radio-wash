using System.Net;
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
using RadioWash.Api.Infrastructure.Authentication;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;
using RadioWash.Api.Hubs;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Services.BackgroundServices;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<SpotifySettings>(builder.Configuration.GetSection(SpotifySettings.SectionName));
builder.Services.Configure<AppleMusicSettings>(builder.Configuration.GetSection(AppleMusicSettings.SectionName));
builder.Services.Configure<RadioWash.Api.Configuration.BatchProcessingSettings>(builder.Configuration.GetSection(RadioWash.Api.Configuration.BatchProcessingSettings.SectionName));
var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://localhost:3000";

// `.dockerignore` keeps appsettings.*.json out of the image, so a containerised Development run
// sees only appsettings.json — which holds production values. Left unchecked that surfaces as
// blanket 401s (tokens validated against the wrong issuer) and CORS-blocked requests, with
// nothing in the logs naming configuration as the cause. Fail fast and say exactly what to set.
if (builder.Environment.IsDevelopment())
{
    LocalDevelopmentConfigurationGuard.Validate(builder.Configuration, frontendUrl);
}

// Services
builder.Services.AddHttpClient();

// Named HttpClient for JWKS fetching with appropriate timeout
builder.Services.AddHttpClient("JwksClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Configure Data Protection with persistent key storage
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<RadioWashDbContext>()
    .SetApplicationName("RadioWash");
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
builder.Services.AddScoped<IMusicTokenService, MusicTokenService>();
// Per-provider token refreshers. MusicTokenService takes IEnumerable<IMusicTokenRefresher>
// and routes by ProviderName, so adding Apple Music is a single AddScoped here — no
// MusicTokenService edits required.
builder.Services.AddScoped<IMusicTokenRefresher, SpotifyTokenRefresher>();

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
// Provider-agnostic music-service adapter. Registered as a keyed IMusicService so the
// IPlaylistCleanerFactory can resolve the right adapter per job.Provider, and as the
// default unkeyed IMusicService for callers that don't yet pick by key.
builder.Services.AddScoped<SpotifyMusicService>();
builder.Services.AddKeyedScoped<IMusicService>(
    SpotifyMusicService.Provider,
    (sp, _) => sp.GetRequiredService<SpotifyMusicService>());
builder.Services.AddScoped<IMusicService>(sp => sp.GetRequiredService<SpotifyMusicService>());
// Typed client so Apple calls get an explicit timeout instead of HttpClient's 100s default.
// Copy jobs issue hundreds of these per playlist; a hung request must not stall a worker for
// over a minute when the service's own retry loop can move on.
builder.Services.AddHttpClient<IAppleMusicService, AppleMusicService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
// Apple compresses some responses (POST /me/library/playlists among them) whether or not the
// request advertises an encoding, and HttpClient does not decompress unless asked. Reading a
// gzip body as text yields "'0x1F' is an invalid start of a value" from the JSON parser —
// the gzip magic byte — which surfaces as a failed copy job well after the write succeeded.
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
});
builder.Services.AddScoped<AppleMusicMusicService>();
builder.Services.AddKeyedScoped<IMusicService>(
    AppleMusicMusicService.Provider,
    (sp, _) => sp.GetRequiredService<AppleMusicMusicService>());
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

// Stripe services (concrete Stripe.net clients; methods are virtual, so tests mock them)
builder.Services.AddScoped<Stripe.CustomerService>();
builder.Services.AddScoped<Stripe.SubscriptionService>();
builder.Services.AddScoped<Stripe.Checkout.SessionService>();
builder.Services.AddScoped<Stripe.BillingPortal.SessionService>();

// Idempotency service for webhook race condition prevention
builder.Services.AddScoped<IIdempotencyService, DatabaseIdempotencyService>();

// Webhook retry services
builder.Services.AddScoped<IWebhookRetryService, WebhookRetryService>();
builder.Services.AddScoped<IWebhookProcessor, StripeWebhookProcessor>();
builder.Services.AddScoped<IErrorClassifier, ErrorClassifier>();

// Time and random abstractions
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddSingleton<IRandomProvider, SystemRandomProvider>();

// Apple Music developer token (ES256 JWT). Singleton so the signed token is cached
// process-wide; missing configuration fails at first use, not at startup, so
// Spotify-only deployments keep booting.
builder.Services.AddSingleton<IAppleDeveloperTokenProvider, AppleDeveloperTokenProvider>();

// SOLID Refactored Services
builder.Services.AddScoped<RadioWash.Api.Infrastructure.Patterns.IUnitOfWork, RadioWash.Api.Infrastructure.Patterns.EntityFrameworkUnitOfWork>();
builder.Services.AddScoped<ICleanPlaylistJobProcessor, CleanPlaylistJobProcessor>();
builder.Services.AddScoped<IJobOrchestrator, HangfireJobOrchestrator>();
builder.Services.AddScoped<IPlaylistCleanerFactory, PlaylistCleanerFactory>();
builder.Services.AddScoped<IMusicServiceFactory, MusicServiceFactory>();
builder.Services.AddScoped<ITrackMatcher, TrackMatcher>();
builder.Services.AddScoped<IPlaylistCopier, PlaylistCopier>();
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

// Authentication - Configure for Supabase JWT using JWKS
// Supabase uses asymmetric keys (ES256) with a JWKS endpoint for token verification.
// This is the recommended approach per Supabase documentation.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    var supabasePublicUrl = builder.Configuration["Supabase:PublicUrl"];
    // Supabase:Url is the internal Docker network URL for container-to-container communication
    // Used for JWKS fetching when running in Docker
    var supabaseInternalUrl = builder.Configuration["Supabase:Url"] ?? supabasePublicUrl;

    // Safety check for missing config
    if (string.IsNullOrEmpty(supabasePublicUrl))
    {
        throw new InvalidOperationException("Supabase:PublicUrl is missing from configuration.");
    }

    // Issuer must match what's in the JWT (the public URL that GoTrue uses)
    var issuer = $"{supabasePublicUrl}/auth/v1";
    // JWKS URL uses internal URL for Docker networking (falls back to public URL if not set)
    var jwksUrl = $"{supabaseInternalUrl}/auth/v1/.well-known/jwks.json";

    // Don't require HTTPS in development (local Supabase uses HTTP)
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
    options.SaveToken = true;
    options.Audience = "authenticated";

    // Configure JWKS key management with caching
    // Use IHttpClientFactory for proper HttpClient lifecycle management
    var httpClientFactory = builder.Services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    var jwksHttpClient = httpClientFactory.CreateClient("JwksClient");
    var jwksLogger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
    JsonWebKeySet? cachedJwks = null;
    DateTime cacheExpiry = DateTime.MinValue;
    var jwksCacheLock = new object();
    var jwksCacheDuration = TimeSpan.FromHours(1); // Cache JWKS for 1 hour

    // HS256 symmetric key used by self-hosted/local GoTrue which signs with GOTRUE_JWT_SECRET.
    // Production Supabase Cloud uses ES256/RS256 via JWKS, but local `supabase start` still issues
    // HS256 tokens because the CLI has not yet enabled asymmetric signing keys. We register the
    // symmetric key alongside JWKS-derived keys in non-production only. In Production we also
    // pin ValidAlgorithms to RS256/ES256 so that a leaked JWT secret cannot forge tokens and
    // algorithm-confusion attacks are ruled out at the validator.
    var hs256Secret = builder.Configuration["Supabase:JwtSecret"];
    SecurityKey? symmetricSigningKey = SupabaseJwtPolicy.AllowHs256(builder.Environment) && !string.IsNullOrEmpty(hs256Secret)
        ? new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(hs256Secret))
        : null;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = "authenticated",
        ClockSkew = TimeSpan.FromMinutes(1),
        ValidAlgorithms = SupabaseJwtPolicy.ValidAlgorithmsFor(builder.Environment),
        // Use IssuerSigningKeyResolver with caching to avoid fetching JWKS on every request
        IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
        {
            IEnumerable<SecurityKey> keys;

            // Double-checked locking: check cache without lock first for performance
            if (cachedJwks != null && DateTime.UtcNow < cacheExpiry)
            {
                keys = cachedJwks.GetSigningKeys();
            }
            else
            {
                lock (jwksCacheLock)
                {
                    if (cachedJwks != null && DateTime.UtcNow < cacheExpiry)
                    {
                        keys = cachedJwks.GetSigningKeys();
                    }
                    else
                    {
                        try
                        {
                            var jwksJson = jwksHttpClient.GetStringAsync(jwksUrl).GetAwaiter().GetResult();
                            cachedJwks = new JsonWebKeySet(jwksJson);
                            cacheExpiry = DateTime.UtcNow.Add(jwksCacheDuration);
                            keys = cachedJwks.GetSigningKeys();
                        }
                        catch (Exception ex)
                        {
                            jwksLogger.LogError(ex, "Failed to fetch JWKS from {Url}", jwksUrl);

                            if (cachedJwks != null)
                            {
                                jwksLogger.LogWarning("Using expired JWKS cache as fallback");
                                keys = cachedJwks.GetSigningKeys();
                            }
                            else if (symmetricSigningKey != null)
                            {
                                // JWKS unreachable and no cache — fall through to HS256-only validation
                                // (covers local dev where JWKS endpoint is empty/unreachable).
                                jwksLogger.LogWarning("JWKS unavailable; validating with HS256 symmetric key only");
                                keys = Array.Empty<SecurityKey>();
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }
                }
            }

            return symmetricSigningKey != null ? keys.Append(symmetricSigningKey) : keys;
        }
    };

    options.Events = new JwtBearerEvents
    {
        // Log authentication failures for debugging
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError("Authentication Failed: {Message}", context.Exception.Message);
            if (context.Exception.InnerException != null)
            {
                logger.LogError("Inner Exception: {InnerMessage}", context.Exception.InnerException.Message);
            }
            return Task.CompletedTask;
        },

        // Log successful token validation
        OnTokenValidated = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Token successfully validated for user: {Subject}",
                context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
            return Task.CompletedTask;
        },

        // Handle SignalR tokens from query string (WebSockets can't use headers)
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
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

    // Global lifecycle logging filter. Registered via the DI container so the filter can
    // consume ILogger<T> like any other service. The resolution runs once here at startup —
    // Hangfire keeps the same instance for every job.
    builder.Services.AddSingleton<RadioWash.Api.Infrastructure.Hangfire.LogJobLifecycleAttribute>();

    // Dashboard authorization: allowlist of Supabase user IDs from configuration. Accepts
    // either a string[] (Hangfire:AdminUserIds:0..n) or a comma-separated string
    // (Hangfire:AdminUserIds). An empty/missing list means the dashboard rejects every user.
    //
    // Access pattern: the filter checks the authenticated Supabase JWT principal, so a browser
    // navigation to /hangfire will fail — browsers do not attach bearer tokens. To triage jobs
    // in production, front the dashboard with a short-lived bearer-injecting proxy (e.g. an
    // ops-only reverse proxy that signs requests with a Supabase service token), or tunnel the
    // port and attach the Authorization header via curl/httpie. Cookie-based access for
    // dashboards is intentionally out of scope — too easy to get CSRF wrong when real money
    // flows through the jobs queue.
    var adminIdsSection = builder.Configuration.GetSection("Hangfire:AdminUserIds");
    var adminIds = adminIdsSection.Get<string[]>()
        ?? (adminIdsSection.Value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    builder.Services.AddSingleton(new RadioWash.Api.Infrastructure.Hangfire.SupabaseAdminAuthorization(adminIds));
    builder.Services.AddSingleton<RadioWash.Api.Infrastructure.Hangfire.SupabaseAdminAuthorizationFilter>();
}

// Background services
builder.Services.AddHostedService<WebhookRetryBackgroundService>();
builder.Services.AddHostedService<SubscriptionExpiryBackgroundService>();
builder.Services.AddHostedService<WebhookTableRetentionBackgroundService>();

// Rate limiting: per-authenticated-user bucket for subscription checkout/portal endpoints.
// The Stripe webhook endpoint is deliberately NOT rate-limited — Stripe retries aggressively
// during outage recovery from a small pool of egress IPs, and a 429 would cause Stripe to
// eventually give up within its retry window, creating the silent state drift we're trying
// to prevent. Signature verification + idempotency already gate that endpoint.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("checkout", httpContext =>
    {
        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? httpContext.User?.FindFirst("sub")?.Value;
        var partitionKey = string.IsNullOrEmpty(userId) ? "__anonymous__" : userId;
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    // checkout/complete is a read-and-sync (no Stripe object creation), and the success
    // page deliberately retries it while Stripe is having a transient outage — up to every
    // other 2s poll tick. The 5/min "checkout" bucket would 429 that retry loop within
    // ~20 seconds and defeat the 500-means-retry design, so it gets its own looser bucket.
    options.AddPolicy("checkout-complete", httpContext =>
    {
        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? httpContext.User?.FindFirst("sub")?.Value;
        var partitionKey = string.IsNullOrEmpty(userId) ? "__anonymous__" : userId;
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        var problem = new
        {
            type = "https://radiowash.app/problems/rate-limit-exceeded",
            title = "Too many requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = "You've issued too many requests in a short period. Please wait a moment and try again."
        };
        await context.HttpContext.Response.WriteAsync(
            System.Text.Json.JsonSerializer.Serialize(problem),
            cancellationToken);
    };
});

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

// Register the lifecycle-logging filter globally. Must happen after Build() so DI can
// resolve the ILogger<T> dependency. Guarded by the same skipHangfire flag used for server
// registration above so test environments don't need to wire the filter.
if (!app.Environment.IsEnvironment("Testing") && !app.Environment.IsEnvironment("Test") && !skipHangfire)
{
    var lifecycleFilter = app.Services.GetRequiredService<RadioWash.Api.Infrastructure.Hangfire.LogJobLifecycleAttribute>();
    global::Hangfire.GlobalJobFilters.Filters.Add(lifecycleFilter);
}

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

        // Detect drift between the configured Stripe price and the seeded plan: the seeder
        // only runs on an empty table, so a rotated Stripe:PricePlanId leaves a stale row
        // that silently breaks subscription-created webhooks (no matching local plan).
        var configuredPriceId = app.Configuration["Stripe:PricePlanId"];
        if (!string.IsNullOrEmpty(configuredPriceId))
        {
            var priceDbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
            var seededPriceIds = await priceDbContext.SubscriptionPlans
                .Where(p => p.IsActive && p.StripePriceId != null)
                .Select(p => p.StripePriceId!)
                .ToListAsync();

            if (seededPriceIds.Count > 0 && !seededPriceIds.Contains(configuredPriceId))
            {
                migrationLogger.LogError(
                    "Stripe:PricePlanId {ConfiguredPriceId} does not match any seeded SubscriptionPlan price ({SeededPriceIds}) - subscription webhooks will fail to resolve a plan",
                    configuredPriceId, string.Join(", ", seededPriceIds));
            }
        }
    }
}

app.UseCors("AllowFrontend");
app.UseMiddleware<RadioWash.Api.Middleware.GlobalExceptionMiddleware>();
app.UseAuthentication();
app.UseMiddleware<RadioWash.Api.Middleware.TokenRefreshMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
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
    var dashboardFilter = app.Services.GetRequiredService<RadioWash.Api.Infrastructure.Hangfire.SupabaseAdminAuthorizationFilter>();
    app.UseHangfireDashboard("/hangfire", new Hangfire.DashboardOptions
    {
        AsyncAuthorization = new[] { dashboardFilter }
    });

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
