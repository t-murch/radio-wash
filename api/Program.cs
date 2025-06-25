using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RadioWash.Api.Configuration;
using RadioWash.Api.Hubs;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Services;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<SpotifySettings>(builder.Configuration.GetSection(SpotifySettings.SectionName));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<SupabaseSettings>(builder.Configuration.GetSection(SupabaseSettings.SectionName));
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>();
var supabaseSettings = builder.Configuration.GetSection(SupabaseSettings.SectionName).Get<SupabaseSettings>();
var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://localhost:3000";

// Services
builder.Services.AddHttpClient();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ISpotifyService, SpotifyService>();
builder.Services.AddScoped<ICleanPlaylistService, CleanPlaylistService>();

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

// Authentication
var supabaseSignatureKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabaseSettings!.SecretKey));
var validIssuer = $"https://{supabaseSettings.ProjectId}.supabase.co/auth/v1";
var validAudiences = new List<string> { "authenticated" };

builder.Services.AddAuthentication(options =>
{
  options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
  options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
  options.TokenValidationParameters = new TokenValidationParameters
  {
    ValidateIssuer = true,
    ValidateAudience = true,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    ValidIssuer = validIssuer,
    ValidAudiences = validAudiences,
    IssuerSigningKey = supabaseSignatureKey
  };

  options.Events = new JwtBearerEvents
  {
    // This event allows us to read the token from a cookie for both API and SignalR
    OnMessageReceived = context =>
    {
      // For SignalR hubs, also check cookies during negotiation
      var path = context.HttpContext.Request.Path;
      if (path.StartsWithSegments("/hubs"))
      {
        context.Token = context.Request.Cookies["rw-auth-token"];
      }
      else
      {
        context.Token = context.Request.Cookies["rw-auth-token"];
      }
      return Task.CompletedTask;
    }
  };
});

// Hangfire
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(config => config.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();

// SignalR
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();
builder.Services.AddSignalR();


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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI();
}

// Apply migrations in development
using (var scope = app.Services.CreateScope())
{
  var dbContext = scope.ServiceProvider.GetRequiredService<RadioWashDbContext>();
  var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

  try
  {
    dbContext.Database.Migrate();
    logger.LogInformation("Database migrations applied successfully");
  }
  catch (Exception ex)
  {
    logger.LogError(ex, "Error applying database migrations");
    throw;
  }
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<JobStatusHub>("/hubs/job-status").RequireCors("AllowFrontend");
app.UseHangfireDashboard();
app.Run();
