using System.Reflection;
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

// Hangfire configuration
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(config => config.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));

// Configuration
builder.Services.Configure<SpotifySettings>(
    builder.Configuration.GetSection(SpotifySettings.SectionName));
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

// Database configuration
builder.Services.AddDbContext<RadioWashDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Services
builder.Services.AddHttpClient();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ISpotifyService, SpotifyService>();
builder.Services.AddScoped<ICleanPlaylistService, CleanPlaylistService>();

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>();
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
    ValidIssuer = jwtSettings.Issuer,
    ValidAudience = jwtSettings.Audience,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
  };
});

// CORS
builder.Services.AddCors(options =>
{
  options.AddPolicy("AllowFrontend", policy =>
  {
    policy.WithOrigins(builder.Configuration["AllowedOrigins"].Split(';'))
          .AllowAnyMethod()
          .AllowAnyHeader()
          .AllowCredentials();
  });
});

// Add services to the container
builder.Services.AddHangfireServer();
builder.Services.AddHealthChecks();
builder.Services.AddHttpsRedirection(options =>
{
  options.HttpsPort = 443;
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
  c.SwaggerDoc("v1", new OpenApiInfo { Title = "RadioWash API", Version = "v1" });

  // Configure Swagger to use JWT Authentication
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

builder.Services.AddHttpLogging(o => { });

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
  app.UseHttpLogging();
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
    throw; // Re-throw to prevent startup with bad database state
  }
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/healthz");
app.MapControllers();

// Configure Hangfire
app.UseHangfireDashboard();
app.Run();
