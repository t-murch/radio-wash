using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RadioWash.Api.Configuration;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Services.Implementations;
using RadioWash.Api.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<SpotifySettings>(builder.Configuration.GetSection(SpotifySettings.SectionName));
var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://localhost:3000";

// Services
builder.Services.AddHttpClient();
builder.Services.AddDataProtection(); // For encryption
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserProviderTokenService, SupabaseUserProviderTokenService>();
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

// Authentication - Configure for Supabase JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
  options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

  var supabaseUrl = builder.Configuration["Supabase:Url"];
  var jwtSecret = builder.Configuration["Supabase:JwtSecret"];
  options.Authority = $"{supabaseUrl}/auth/v1";
  options.Audience = "authenticated";
  options.TokenValidationParameters = new TokenValidationParameters
  {
    ValidateIssuer = true,
    ValidateAudience = true,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    ValidIssuer = $"{supabaseUrl}/auth/v1",
    ValidAudience = "authenticated",
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret!))
  };

  options.Events = new JwtBearerEvents
  {
    OnMessageReceived = context =>
    {
      // Read token from Authorization header or hash fragment for auth callback
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

// Hangfire
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(config => config.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();

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
app.UseHangfireDashboard();
app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
