# Supabase Authentication Refactor Plan

## Overview

This document outlines the plan to refactor RadioWash's authentication system from custom Spotify OAuth + JWT to Supabase Auth, with support for multiple music service providers (Spotify and Apple Music).

## Current vs New Architecture

### Current State
- **Authentication:** Custom Spotify OAuth flow
- **Authorization:** Custom JWT tokens stored in HttpOnly cookies
- **Music Access:** Spotify API only
- **User Management:** Custom user creation from Spotify profiles

### Target State
- **Authentication:** Supabase Auth (email/password, social providers)
- **Authorization:** Supabase JWT tokens validated server-side
- **Music Access:** Multi-platform support (Spotify + Apple Music)
- **User Management:** Supabase user profiles with local synchronization

## Architecture Design

### Core Principles
1. **Separation of Concerns:** User authentication vs music service authorization
2. **Multi-Platform Ready:** Support for multiple music services per user
3. **Scalable:** Easy addition of new music services
4. **Security First:** Leverage Supabase's proven authentication infrastructure

### Component Structure

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Supabase      │    │   RadioWash      │    │  Music Services │
│   Auth          │    │   Backend        │    │  (Spotify/Apple)│
├─────────────────┤    ├──────────────────┤    ├─────────────────┤
│ • User Auth     │◄──►│ • JWT Validation │◄──►│ • OAuth Flows   │
│ • Session Mgmt  │    │ • User Sync      │    │ • API Access    │
│ • Token Refresh │    │ • Service Mgmt   │    │ • Token Refresh │
└─────────────────┘    └──────────────────┘    └─────────────────┘
```

## Implementation Plan

### Phase 1: Backend Authentication Configuration

#### 1.1 Update JWT Configuration (`api/Program.cs`)
```csharp
var supabaseSignatureKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabaseSecretKey));
var validIssuer = "https://<project-id>.supabase.co/auth/v1";
var validAudiences = new List<string>() { "authenticated" };

builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = supabaseSignatureKey,
        ValidAudiences = validAudiences,
        ValidIssuer = validIssuer
    };
});
```

#### 1.2 Authentication Controller Refactor (`api/Controllers/AuthController.cs`)
**New Endpoints:**
- `POST /api/auth/signup` - Supabase user registration
- `POST /api/auth/signin` - Supabase email/password login
- `POST /api/auth/signout` - Supabase session cleanup
- `GET /api/auth/spotify/auth` - Spotify OAuth initiation
- `GET /api/auth/spotify/callback` - Spotify OAuth callback
- `GET /api/auth/apple/auth` - Apple Music OAuth initiation
- `GET /api/auth/apple/callback` - Apple Music OAuth callback
- `DELETE /api/auth/services/{service}` - Disconnect music service

### Phase 2: Service Architecture

#### 2.1 AuthService (Supabase Integration)
**File:** `api/Services/Implementations/AuthService.cs`
**Responsibilities:**
- Supabase user authentication
- JWT token validation
- User profile synchronization with local database

#### 2.2 MusicServiceAuthService (New)
**File:** `api/Services/Implementations/MusicServiceAuthService.cs`
**Responsibilities:**
- Multi-platform OAuth flow management
- Service connection status tracking
- Token refresh coordination

#### 2.3 Music Service Providers (New)
**Abstract Interface:** `IMusicServiceProvider`
**Implementations:**
- `SpotifyMusicProvider`
- `AppleMusicProvider`

### Phase 3: Database Schema Updates

#### 3.1 User Model Refactor
```csharp
public class User
{
    public int Id { get; set; }
    public Guid SupabaseUserId { get; set; }  // Primary auth identifier
    public string Email { get; set; }
    public string DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public ICollection<UserMusicService> MusicServices { get; set; }
}
```

#### 3.2 UserMusicService Model (New)
```csharp
public class UserMusicService
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public MusicServiceType ServiceType { get; set; } // Spotify, AppleMusic
    public string ServiceUserId { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    
    public User User { get; set; }
}

public enum MusicServiceType
{
    Spotify,
    AppleMusic
}
```

### Phase 4: Frontend Integration

#### 4.1 Authentication Context Updates
**File:** `web/src/app/contexts/Authcontext.tsx`
```typescript
interface AuthContextType {
  user: User | null;
  connectedServices: MusicService[];
  signIn: (email: string, password: string) => Promise<void>;
  signUp: (email: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
  connectSpotify: () => Promise<void>;
  connectAppleMusic: () => Promise<void>;
  disconnectService: (service: MusicServiceType) => Promise<void>;
}
```

#### 4.2 Music Service Management UI (New)
**File:** `web/src/app/components/MusicServiceConnections.tsx`
- Service connection status display
- Connect/disconnect buttons
- Primary service selection
- Scope management

## Configuration Requirements

### Environment Variables
```bash
# Supabase Configuration
SUPABASE_URL=https://<project-id>.supabase.co
SUPABASE_ANON_KEY=<anon-key>
SUPABASE_SECRET_KEY=<service-role-key>

# Spotify Configuration
SPOTIFY_CLIENT_ID=<client-id>
SPOTIFY_CLIENT_SECRET=<client-secret>

# Apple Music Configuration
APPLE_MUSIC_KEY_ID=<key-id>
APPLE_MUSIC_TEAM_ID=<team-id>
APPLE_MUSIC_PRIVATE_KEY=<private-key>
```

### Supabase Project Setup
1. **Authentication Providers:** Email/password, social logins
2. **Row Level Security:** User-specific data access policies
3. **Database Schema:** Sync with RadioWash user management
4. **Realtime (Optional):** Live session updates

## Security Considerations

### Token Management
- Supabase JWT validation using provided configuration
- Maintain HttpOnly cookie pattern for XSS protection
- Implement proper CSRF protection for OAuth flows

### Multi-Service Authorization
- Separate OAuth scopes for each music service
- Secure token storage with encryption at rest
- Token refresh with proper error handling

### User Data Synchronization
- Sync Supabase user metadata with local User table
- Maintain referential integrity
- Handle edge cases (service disconnections, token expiry)

## Implementation Benefits

1. **Clean Architecture:** Clear separation between user auth and service authorization
2. **Multi-Platform Support:** Users can connect multiple music services simultaneously
3. **Scalability:** Easy addition of new music services (YouTube Music, Tidal, etc.)
4. **Security:** Leverages Supabase's proven authentication infrastructure
5. **User Experience:** Unified authentication with flexible music service options
6. **Maintainability:** Reduced custom authentication code to maintain

## Migration Strategy

Since there are no existing users, this refactor will:
1. Remove all custom authentication code
2. Implement clean Supabase integration
3. Build multi-platform music service support from ground up
4. Establish patterns for future service additions

## Files to Modify

### Backend Files
- `api/Program.cs` - JWT configuration update
- `api/Controllers/AuthController.cs` - Complete refactor
- `api/Services/Implementations/AuthService.cs` - Supabase integration
- `api/Services/Implementations/TokenService.cs` - Music service focus
- `api/Models/Domain/User.cs` - Schema updates
- `api/Infrastructure/Data/RadioWashDbContext.cs` - New entities

### New Backend Files
- `api/Services/Implementations/MusicServiceAuthService.cs`
- `api/Models/Domain/UserMusicService.cs`
- `api/Services/Interfaces/IMusicServiceProvider.cs`
- `api/Services/Implementations/SpotifyMusicProvider.cs`
- `api/Services/Implementations/AppleMusicProvider.cs`

### Frontend Files
- `web/src/app/contexts/Authcontext.tsx` - Supabase integration
- `web/src/app/services/api.ts` - Updated auth headers
- `web/src/app/services/auth.ts` - Supabase client integration

### New Frontend Files
- `web/src/app/components/MusicServiceConnections.tsx`
- `web/src/app/services/supabase.ts`

## Testing Strategy

1. **Unit Tests:** Service layer authentication logic
2. **Integration Tests:** OAuth flows for each music service
3. **E2E Tests:** Complete user registration and service connection flows
4. **Security Tests:** Token validation and session management

## Future Enhancements

1. **Additional Music Services:** YouTube Music, Tidal, Amazon Music
2. **Cross-Platform Sync:** Playlist synchronization between services
3. **Service Analytics:** Usage statistics per connected service
4. **Advanced Scoping:** Granular permissions per music service