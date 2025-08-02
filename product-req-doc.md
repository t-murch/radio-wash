# RadioWash Technical Specification

## Product Overview

**RadioWash** is a web application that creates "clean" versions of music playlists by automatically replacing explicit tracks with their clean alternatives when available. Users can connect their Spotify or Apple Music accounts, select playlists to "wash," preview the results, and create new clean playlists. A $2/month subscription enables ongoing synchronization for both user playlists and popular public playlists.

---

## Core Features

### 1. Playlist Washing Process

- **Authentication**: OAuth integration with Spotify and Apple Music APIs
- **Playlist Selection**: Users select one playlist at a time to process
- **Track Analysis**: Identify explicit tracks using platform APIs (`explicit` boolean for Spotify, `contentRating` for Apple Music)
- **Clean Alternative Matching**: Find non-explicit versions of explicit tracks
- **Preview Functionality**: Show proposed changes before creating clean playlist
- **Clean Playlist Creation**: Generate new playlist with clean alternatives, exclude tracks without clean versions
- **Progress Tracking**: Real-time job status updates via SignalR
- **Results Summary**: Display clean-match/total-track ratio and transformation details

### 2. Subscription Services ($2/month)

- **User Playlist Sync**: Automatically sync changes from original playlists to their clean versions
- **Public Playlist Sync**: Create and maintain clean versions of popular public playlists (e.g., "Today's Top Hits")
- **Sync Frequency**: Daily automated checks + manual trigger button
- **Sync Logic**: Attempt to find clean alternatives for newly added explicit tracks

### 3. Job Processing

- **Asynchronous Processing**: All playlist operations run as background jobs
- **Concurrent Jobs**: Users can queue multiple playlist washing jobs
- **Rate Limiting**: Respect music platform API limits
- **Retry Logic**: Handle temporary API failures with exponential backoff
- **Job Status Tracking**: Detailed progress, error messages, and completion status

---

## Technical Architecture

### Technology Stack

**Backend:**

- ASP.NET Core 8 API
- Entity Framework Core with PostgreSQL (via Supabase)
- Hangfire for background job processing
- SignalR for real-time updates
- Swagger/OpenAPI for API documentation

**Frontend:**

- Next.js 15 with React
- TypeScript
- ShadCN UI components
- Tailwind CSS

**Infrastructure:**

- NX Monorepo structure
- PostgreSQL database via Supabase
- Supabase Auth for authentication
- Docker containerization
- Hangfire dashboard for job monitoring

**External Integrations:**

- Spotify Web API
- Apple Music API
- Payment processing (Stripe recommended)

### Architecture Pattern

**Clean Architecture Implementation:**

```
├── RadioWash.Api (Presentation Layer)
│   ├── Controllers/
│   ├── Middleware/
│   ├── Hubs/ (SignalR)
│   └── Program.cs
├── RadioWash.Application (Application Layer)
│   ├── Services/
│   ├── Interfaces/
│   ├── DTOs/
│   └── Commands/Queries/
├── RadioWash.Domain (Domain Layer)
│   ├── Entities/
│   ├── Enums/
│   ├── Interfaces/
│   └── ValueObjects/
├── RadioWash.Infrastructure (Infrastructure Layer)
│   ├── Data/ (EF Core DbContext)
│   ├── Repositories/
│   ├── ExternalServices/ (Spotify, Apple Music APIs)
│   ├── BackgroundJobs/
│   └── Security/
└── RadioWash.Shared
    ├── Constants/
    ├── Utilities/
    └── Extensions/
```

---

## Data Model

### Core Entities

```csharp
public class User : ISoftDelete
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation Properties
    public List<Playlist> Playlists { get; set; } = [];
    public List<UserSubscription> Subscriptions { get; set; } = [];
    public List<ExternalAccount> ExternalAccounts { get; set; } = [];
}

public class ExternalAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ExternalPlatform Platform { get; set; } // Spotify, AppleMusic
    public string ExternalUserId { get; set; } = null!;

    [EncryptColumn] // Field-level encryption
    public string? AccessToken { get; set; }
    [EncryptColumn]
    public string? RefreshToken { get; set; }

    public DateTime? TokenExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}

public class Playlist : ISoftDelete
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public PlaylistType Type { get; set; } // Original, Clean, PublicClean
    public Guid? OriginalPlaylistId { get; set; } // For clean versions
    public string? ExternalPlaylistId { get; set; } // Platform-specific ID
    public ExternalPlatform? Platform { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation Properties
    public User User { get; set; } = null!;
    public Playlist? OriginalPlaylist { get; set; }
    public List<Playlist> CleanVersions { get; set; } = [];
    public List<PlaylistTrack> PlaylistTracks { get; set; } = [];
    public List<PlaylistProcessingJob> ProcessingJobs { get; set; } = [];
}

public class PlaylistTrack
{
    public Guid Id { get; set; }
    public Guid PlaylistId { get; set; }
    public Guid TrackId { get; set; }
    public int Position { get; set; }
    public DateTime AddedAt { get; set; }

    public Playlist Playlist { get; set; } = null!;
    public Track Track { get; set; } = null!;
}

public class Track
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string Artist { get; set; } = null!;
    public string? Album { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool IsExplicit { get; set; }
    public string? ExternalTrackId { get; set; } // Platform-specific ID
    public ExternalPlatform? Platform { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public List<PlaylistTrack> PlaylistTracks { get; set; } = [];
}

public class PlaylistProcessingJob
{
    public Guid Id { get; set; }
    public string HangfireJobId { get; set; } = null!;
    public Guid PlaylistId { get; set; }
    public JobType JobType { get; set; } // Wash, Sync
    public JobStatus Status { get; set; } // Pending, Processing, Completed, Failed
    public int TotalTracks { get; set; }
    public int ProcessedTracks { get; set; }
    public int CleanTracksFound { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryAttempts { get; set; }
    public string? ResultSummary { get; set; } // JSON with detailed results

    public Playlist Playlist { get; set; } = null!;
}

public class UserSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public SubscriptionStatus Status { get; set; } // Active, Cancelled, Expired
    public decimal MonthlyPrice { get; set; } = 2.00m;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime NextBillingDate { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? StripeCustomerId { get; set; }

    public User User { get; set; } = null!;
}
```

### Enums

```csharp
public enum ExternalPlatform
{
    Spotify = 1,
    AppleMusic = 2
}

public enum PlaylistType
{
    Original = 1,
    Clean = 2,
    PublicClean = 3
}

public enum JobType
{
    PlaylistWash = 1,
    PlaylistSync = 2,
    PublicPlaylistSync = 3
}

public enum JobStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}

public enum SubscriptionStatus
{
    Active = 1,
    Cancelled = 2,
    Expired = 3,
    PastDue = 4
}
```

---

## API Endpoints

### Authentication & User Management

```
POST   /api/auth/login
POST   /api/auth/logout
GET    /api/auth/user
POST   /api/auth/connect/{platform}    # OAuth connection to Spotify/Apple Music
DELETE /api/auth/disconnect/{platform}
```

### Playlist Management

```
GET    /api/playlists                  # Get user's playlists
GET    /api/playlists/{id}
POST   /api/playlists/{id}/wash        # Start playlist washing job
GET    /api/playlists/{id}/preview     # Preview clean version
POST   /api/playlists/{id}/create-clean # Create clean playlist from preview
GET    /api/playlists/{id}/jobs        # Get processing jobs for playlist
```

### Job Management

```
GET    /api/jobs                       # Get user's jobs
GET    /api/jobs/{id}                  # Get specific job details
POST   /api/jobs/{id}/cancel
GET    /api/jobs/{id}/retry
```

### Subscription Management

```
GET    /api/subscriptions              # Get user's subscription
POST   /api/subscriptions/create       # Create new subscription
POST   /api/subscriptions/cancel
POST   /api/subscriptions/update-payment
GET    /api/subscriptions/usage        # Get sync usage statistics
```

### Public Playlists (Subscription Feature)

```
GET    /api/public-playlists           # Get available public playlists
POST   /api/public-playlists/{id}/sync # Sync public playlist
GET    /api/public-playlists/my-synced # Get user's synced public playlists
```

### Sync Management (Subscription Feature)

```
POST   /api/sync/trigger               # Manual sync trigger
GET    /api/sync/status                # Get sync status
GET    /api/sync/history              # Get sync history
```

---

## Application Services

### Core Application Services

```csharp
public interface IPlaylistWashingService
{
    Task<JobResult> StartPlaylistWashAsync(Guid playlistId, string userId);
    Task<PreviewResult> PreviewCleanPlaylistAsync(Guid playlistId, string userId);
    Task<PlaylistResult> CreateCleanPlaylistAsync(Guid playlistId, string playlistName, string userId);
    Task<SyncResult> SyncPlaylistAsync(Guid playlistId, string userId);
}

public interface IExternalMusicService
{
    Task<List<PlaylistSummary>> GetUserPlaylistsAsync(ExternalPlatform platform, string userId);
    Task<PlaylistDetails> GetPlaylistDetailsAsync(ExternalPlatform platform, string playlistId, string userId);
    Task<TrackMatchResult> FindCleanAlternativeAsync(ExternalPlatform platform, TrackInfo track, string userId);
    Task<PlaylistCreationResult> CreatePlaylistAsync(ExternalPlatform platform, CreatePlaylistRequest request, string userId);
    Task<bool> RefreshTokenAsync(ExternalAccount account);
}

public interface IJobProgressService
{
    Task UpdateJobProgressAsync(Guid jobId, int processedTracks, int totalTracks);
    Task CompleteJobAsync(Guid jobId, JobCompletionResult result);
    Task FailJobAsync(Guid jobId, string errorMessage, int retryAttempt);
    Task NotifyJobProgressAsync(string userId, JobProgressUpdate update);
}

public interface ISubscriptionService
{
    Task<SubscriptionResult> CreateSubscriptionAsync(string userId, PaymentMethodInfo paymentMethod);
    Task<bool> CancelSubscriptionAsync(string userId);
    Task<bool> IsUserSubscribedAsync(string userId);
    Task<UsageStatistics> GetUsageStatisticsAsync(string userId);
    Task ProcessSubscriptionWebhookAsync(StripeWebhookEvent webhookEvent);
}
```

### Background Job Services

```csharp
public interface IPlaylistWashingJob
{
    [Queue("playlist-processing")]
    Task ExecuteAsync(Guid playlistId, string userId, Guid jobId);
}

public interface IPlaylistSyncJob
{
    [Queue("playlist-sync")]
    Task ExecuteAsync(Guid playlistId, string userId, Guid jobId);
}

public interface IPublicPlaylistSyncJob
{
    [Queue("public-sync")]
    Task ExecuteAsync(string publicPlaylistId, ExternalPlatform platform);
}

public interface IDailyMaintenanceJob
{
    [Queue("maintenance")]
    Task ExecuteAsync();
    Task SyncSubscriberPlaylistsAsync();
    Task CleanupExpiredJobsAsync();
    Task RefreshExpiringTokensAsync();
}
```

---

## Security & Data Protection

### Token Security

- **Encryption**: All OAuth tokens encrypted at rest using PostgreSQL pgcrypto
- **Token Rotation**: Automatic refresh of access tokens before expiration
- **Secure Storage**: Refresh tokens stored with additional encryption layer
- **Audit Trail**: Track all token access and refresh operations

### Data Privacy

- **Soft Deletes**: Implement soft deletes for user data recovery and compliance
- **Data Retention**: Automated cleanup after 30 days (playlists) and 1 year (user data)
- **GDPR Compliance**: Data export functionality and right to deletion
- **Access Control**: Row-level security policies in Supabase

### API Security

- **Rate Limiting**: Implement rate limiting for all API endpoints
- **Authentication**: JWT-based authentication with refresh tokens
- **Authorization**: Role-based access control for subscription features
- **Input Validation**: Comprehensive validation for all API inputs

---

## Background Job Processing

### Job Queue Configuration

```csharp
// Hangfire Configuration
services.AddHangfire(config =>
{
    config.UsePostgreSqlStorage(connectionString);
    config.UseSimpleAssemblyNameTypeSerializer();
    config.UseRecommendedSerializerSettings();
});

// Job Queues
services.AddHangfireServer(options =>
{
    options.Queues = new[] { "playlist-processing", "playlist-sync", "public-sync", "maintenance" };
    options.WorkerCount = Environment.ProcessorCount * 2;
});
```

### Job Types and Scheduling

- **Playlist Washing**: User-triggered, immediate processing
- **Playlist Sync**: Daily scheduled + manual trigger
- **Public Playlist Sync**: Daily scheduled for popular playlists
- **Token Refresh**: Every 30 minutes, check for expiring tokens
- **Maintenance**: Daily cleanup of old jobs and data retention

### Error Handling and Retries

- **Exponential Backoff**: Retry failed API calls with increasing delays
- **Dead Letter Queue**: Move persistently failing jobs to separate queue
- **Error Notifications**: Real-time error notifications via SignalR
- **Monitoring**: Comprehensive logging and alerting for job failures

---

## Performance Optimization

### Database Optimization

```csharp
// Strategic Indexes
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Fast playlist lookups
    modelBuilder.Entity<Playlist>()
        .HasIndex(p => new { p.UserId, p.Type, p.IsDeleted });

    // Job monitoring queries
    modelBuilder.Entity<PlaylistProcessingJob>()
        .HasIndex(j => new { j.Status, j.StartedAt });

    // Subscription billing queries
    modelBuilder.Entity<UserSubscription>()
        .HasIndex(s => new { s.Status, s.NextBillingDate });

    // Track search optimization
    modelBuilder.Entity<Track>()
        .HasIndex(t => new { t.Title, t.Artist, t.Platform });
}
```

### Caching Strategy

- **Redis Cache**: Cache frequently accessed playlist data and track metadata
- **Memory Cache**: Cache external API responses for short periods
- **CDN**: Static assets and API documentation

### API Rate Limiting

- **External APIs**: Respect Spotify (100 requests/minute) and Apple Music rate limits
- **Internal APIs**: Implement user-based rate limiting to prevent abuse
- **Queue Management**: Distribute API calls across time to maintain consistent throughput

---

## Deployment & Infrastructure

### Docker Configuration

```dockerfile
# API Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["RadioWash.Api/RadioWash.Api.csproj", "RadioWash.Api/"]
RUN dotnet restore "RadioWash.Api/RadioWash.Api.csproj"
COPY . .
WORKDIR "/src/RadioWash.Api"
RUN dotnet build "RadioWash.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RadioWash.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RadioWash.Api.dll"]
```

### Environment Configuration

- **Development**: Local Docker containers with Supabase local setup
- **Staging**: Container deployment with managed PostgreSQL
- **Production**: Container orchestration with auto-scaling
- **Database**: PostgreSQL via Supabase with automated backups

### Monitoring & Observability

- **Application Monitoring**: Comprehensive logging with structured logs
- **Health Checks**: API, database, and external service health endpoints
- **Metrics**: Job processing rates, API response times, error rates
- **Alerting**: Real-time alerts for failures and performance degradation

---

## Testing Strategy

### Unit Testing

- **Service Layer**: Comprehensive unit tests for all application services
- **Repository Layer**: Test data access patterns and queries
- **External Services**: Mock external API interactions
- **Domain Logic**: Test business rules and validation logic

### Integration Testing

- **API Endpoints**: Full request/response cycle testing
- **Database Operations**: Test EF Core migrations and queries
- **Background Jobs**: Test job processing and error handling
- **Authentication**: Test OAuth flows and token management

### End-to-End Testing

- **User Workflows**: Complete playlist washing workflows
- **Subscription Flows**: Payment processing and feature access
- **Error Scenarios**: API failures and recovery mechanisms
- **Performance Testing**: Load testing for concurrent users

---

## Future Considerations

### Scalability Improvements

- **Microservices**: Split into separate services for playlist processing, user management, and billing
- **Message Queues**: Replace Hangfire with RabbitMQ or Azure Service Bus for better scalability
- **Caching Layer**: Implement distributed caching for improved performance
- **CDN Integration**: Optimize static asset delivery

### Feature Enhancements

- **Additional Platforms**: Support for YouTube Music, Amazon Music, Tidal
- **AI-Powered Matching**: Machine learning for better clean track matching
- **Collaborative Playlists**: Support for shared playlist washing
- **Analytics Dashboard**: User insights and usage analytics

### Technical Debt Management

- **Code Coverage**: Maintain >80% test coverage across all layers
- **Performance Monitoring**: Continuous monitoring and optimization
- **Security Audits**: Regular security assessments and penetration testing
- **Dependency Updates**: Automated dependency updates and security patches

---

## Development Workflow

### Git Strategy

- **Main Branch**: Production-ready code
- **Develop Branch**: Integration branch for features
- **Feature Branches**: Individual feature development
- **Release Branches**: Version preparation and bug fixes

### CI/CD Pipeline

1. **Code Commit**: Push to feature branch
2. **Automated Testing**: Run unit and integration tests
3. **Code Quality**: Static analysis and code coverage checks
4. **Build**: Create Docker images for API and frontend
5. **Deploy to Staging**: Automated deployment to staging environment
6. **Manual Testing**: User acceptance testing
7. **Deploy to Production**: Manual approval and deployment

### Code Quality Standards

- **Code Reviews**: All code changes require peer review
- **Linting**: Automated code formatting and style checking
- **Documentation**: Comprehensive API documentation with Swagger
- **Performance**: Monitor and optimize for <200ms API response times

This technical specification provides a comprehensive blueprint for building RadioWash as a scalable, secure, and maintainable music playlist processing application.
